using UAM.Core.Models;

namespace UAM.Application;

public sealed record WatchedFolderScanResult
{
    public int ImportedCount { get; init; }

    public int DuplicateCount { get; init; }

    public int RejectedCount { get; init; }

    public int FailedCount { get; init; }

    public required IReadOnlyList<string> Messages { get; init; }
}

public interface IWatchedFolderImportService : IAsyncDisposable
{
    string FolderPath { get; }

    Task<WatchedFolderScanResult> ScanOnceAsync(
        CancellationToken cancellationToken = default);

    void Start();
}

/// <summary>
/// Watches a folder for canonical training documents. Scanning is safe to
/// repeat: the import layer identifies content, so a file can be picked up on
/// create and again on a later scan without duplicating observations.
/// </summary>
public sealed class WatchedFolderImportService : IWatchedFolderImportService
{
    private readonly IImportService _importService;
    private readonly SemaphoreSlim _scanGate = new(1, 1);
    private FileSystemWatcher? _watcher;
    private bool _disposed;

    public WatchedFolderImportService(
        IImportService importService,
        string folderPath)
    {
        _importService = importService
            ?? throw new ArgumentNullException(nameof(importService));
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);

        FolderPath = Path.GetFullPath(folderPath);
    }

    public string FolderPath { get; }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_watcher is not null)
        {
            return;
        }

        if (!Directory.Exists(FolderPath))
        {
            Directory.CreateDirectory(FolderPath);
        }

        _watcher = new FileSystemWatcher(FolderPath, "*.json")
        {
            IncludeSubdirectories = false,
            NotifyFilter =
                NotifyFilters.FileName |
                NotifyFilters.LastWrite |
                NotifyFilters.Size,
            EnableRaisingEvents = true
        };

        _watcher.Created += OnFileChanged;
        _watcher.Changed += OnFileChanged;
        _watcher.Renamed += OnFileChanged;

        // Pick up documents that were present before the watcher started.
        _ = ScanQuietlyAsync();
    }

    public async Task<WatchedFolderScanResult> ScanOnceAsync(
        CancellationToken cancellationToken = default)
    {
        await _scanGate.WaitAsync(cancellationToken);

        try
        {
            if (!Directory.Exists(FolderPath))
            {
                return new WatchedFolderScanResult { Messages = [] };
            }

            var imported = 0;
            var duplicates = 0;
            var rejected = 0;
            var failed = 0;
            var messages = new List<string>();

            var files = Directory
                .EnumerateFiles(FolderPath, "*.json", SearchOption.TopDirectoryOnly)
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToList();

            foreach (var file in files)
            {
                try
                {
                    var result = await _importService.ImportFileAsync(
                        file,
                        cancellationToken);

                    switch (result.Outcome)
                    {
                        case ImportOutcome.Imported:
                            imported++;
                            break;
                        case ImportOutcome.Duplicate:
                            duplicates++;
                            break;
                        default:
                            rejected++;
                            messages.Add($"{Path.GetFileName(file)}: {result.Detail}");
                            break;
                    }
                }
                catch (Exception exception) when (
                    exception is not OperationCanceledException)
                {
                    failed++;
                    messages.Add($"{Path.GetFileName(file)}: {exception.Message}");
                }
            }

            return new WatchedFolderScanResult
            {
                ImportedCount = imported,
                DuplicateCount = duplicates,
                RejectedCount = rejected,
                FailedCount = failed,
                Messages = messages
            };
        }
        finally
        {
            _scanGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_watcher is not null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Created -= OnFileChanged;
            _watcher.Changed -= OnFileChanged;
            _watcher.Renamed -= OnFileChanged;
            _watcher.Dispose();
            _watcher = null;
        }

        await _scanGate.WaitAsync();
        _scanGate.Release();
        _scanGate.Dispose();
    }

    private void OnFileChanged(object sender, FileSystemEventArgs args)
    {
        _ = ScanQuietlyAsync();
    }

    private async Task ScanQuietlyAsync()
    {
        try
        {
            await ScanOnceAsync();
        }
        catch (Exception)
        {
            // The watcher has no UI to report to. A failed scan is retried on
            // the next file event; manual imports surface their own errors.
        }
    }
}
