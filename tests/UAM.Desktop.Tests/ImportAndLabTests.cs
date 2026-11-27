using UAM.Application;
using UAM.Core.Models;
using UAM.Core.Serialization;
using UAM.Data;
using UAM.Desktop.ViewModels;

namespace UAM.Desktop.Tests;

public sealed class ImportAndLabTests : IDisposable
{
    private readonly string _directory;
    private readonly string _databasePath;
    private readonly TrainingStoreFactory _factory;
    private readonly ImportService _importService;

    public ImportAndLabTests()
    {
        _directory = Path.Combine(
            Path.GetTempPath(),
            "uam-import-tests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(_directory);
        _databasePath = Path.Combine(_directory, "uam.db");
        _factory = new TrainingStoreFactory(_databasePath);
        _importService = new ImportService(_factory);
    }

    private static TrainingObservation Observation(
        string sessionId,
        int index,
        string gameId = "valorant") => new()
    {
        SessionId = sessionId,
        GameId = gameId,
        ScenarioId = "gridshot",
        TimestampUtc = DateTime.UtcNow.AddMinutes(-10).AddSeconds(index),
        DirectionDegrees = 0,
        SpeedPixelsPerSecond = 650,
        SignedErrorRatio = 0.12,
        AcquisitionTimeMs = 320,
        Hit = true,
        MovementKind = MovementKind.Flick
    };

    private static TrainingDocument Document(
        string sessionId,
        int count = 3,
        string provider = ProviderRegistry.CanonicalJson) => new()
    {
        Provider = provider,
        CaptureMethod = "json-import",
        SourceSessionId = sessionId,
        Observations = Enumerable.Range(0, count)
            .Select(index => Observation(sessionId, index))
            .ToList()
    };

    [Fact]
    public void CanonicalDocument_RoundTripsWithProvenance()
    {
        var document = Document("session-round-trip", 2);

        var json = TrainingDocumentJson.Serialize(document);
        var loaded = TrainingDocumentJson.Deserialize(json);

        Assert.Equal(TrainingDocumentFormat.CurrentSchemaVersion, loaded.SchemaVersion);
        Assert.Equal(ProviderRegistry.CanonicalJson, loaded.Provider);
        Assert.Equal("json-import", loaded.CaptureMethod);
        Assert.Equal("session-round-trip", loaded.SourceSessionId);
        Assert.Equal(2, loaded.Observations.Count);
        Assert.Equal(document.Observations[0], loaded.Observations[0]);
    }

    [Fact]
    public void LegacyObservationArray_IsWrappedInACanonicalEnvelope()
    {
        var json = ObservationJson.SerializeMany(
            [Observation("legacy-session", 0)]);

        var document = TrainingDocumentJson.Deserialize(json);

        Assert.Equal(ProviderRegistry.CanonicalJson, document.Provider);
        Assert.Equal("legacy-json", document.CaptureMethod);
        Assert.Equal("legacy-session", document.SourceSessionId);
        Assert.Single(document.Observations);
    }

    [Fact]
    public void UnsupportedMajorVersion_IsRejected()
    {
        var json = TrainingDocumentJson.Serialize(Document("future-session"))
            .Replace(
                $"\"schemaVersion\": \"{TrainingDocumentFormat.CurrentSchemaVersion}\"",
                "\"schemaVersion\": \"2.0\"");

        Assert.Throws<TrainingDocumentVersionException>(
            () => TrainingDocumentJson.Deserialize(json));
    }

    [Fact]
    public void ContentHash_IsStableAndLowercase()
    {
        var first = TrainingDocumentJson.ComputeContentHash("{\"a\":1}");
        var second = TrainingDocumentJson.ComputeContentHash("{\"a\":1}");
        var different = TrainingDocumentJson.ComputeContentHash("{\"a\":2}");

        Assert.Equal(first, second);
        Assert.NotEqual(first, different);
        Assert.Equal(64, first.Length);
        Assert.Equal(first.ToLowerInvariant(), first);
    }

    [Fact]
    public async Task ReimportingTheSameDocument_WritesNoDuplicateObservations()
    {
        var json = TrainingDocumentJson.Serialize(Document("session-idempotent", 3));

        var first = await _importService.ImportContentAsync(json);
        var second = await _importService.ImportContentAsync(json);

        Assert.Equal(ImportOutcome.Imported, first.Outcome);
        Assert.Equal(3, first.SavedCount);
        Assert.Equal(ImportOutcome.Duplicate, second.Outcome);
        Assert.Equal(0, second.SavedCount);
        Assert.Equal(2, second.Record!.ImportCount);

        await using var store = new TrainingStore(_databasePath);
        await store.InitializeAsync();

        var session = Assert.Single(await store.GetSessionsAsync());
        Assert.Equal(3, session.ObservationCount);
        Assert.Equal(3, (await store.LoadObservationsAsync()).Count);
    }

    [Fact]
    public async Task ReimportingTheSameSessionWithNewWhitespace_IsAlsoDuplicate()
    {
        var document = Document("session-whitespace", 2);
        var compact = TrainingDocumentJson.Serialize(document);
        var spaced = compact.Replace("  ", "    ");

        var first = await _importService.ImportContentAsync(compact);
        var second = await _importService.ImportContentAsync(spaced);

        Assert.Equal(ImportOutcome.Imported, first.Outcome);
        Assert.Equal(ImportOutcome.Duplicate, second.Outcome);
    }

    [Fact]
    public async Task NotPermittedProvider_IsRefusedBeforeAnythingIsStored()
    {
        var json = TrainingDocumentJson.Serialize(
            Document("riot-session", provider: ProviderRegistry.RiotTelemetry));

        await Assert.ThrowsAsync<ProviderNotPermittedException>(
            () => _importService.ImportContentAsync(json));

        await using var store = new TrainingStore(_databasePath);
        await store.InitializeAsync();
        Assert.Empty(await store.GetSessionsAsync());
        Assert.Empty(await store.GetImportRecordsAsync());
    }

    [Fact]
    public async Task WatchedFolder_ImportsNewFilesAndIgnoresRepeats()
    {
        var folder = Path.Combine(_directory, "imports");
        Directory.CreateDirectory(folder);

        var path = Path.Combine(folder, "session.json");
        await File.WriteAllTextAsync(
            path,
            TrainingDocumentJson.Serialize(Document("watched-session", 2)));

        await using var watcher = new WatchedFolderImportService(
            _importService,
            folder);

        var first = await watcher.ScanOnceAsync();
        var second = await watcher.ScanOnceAsync();

        Assert.Equal(1, first.ImportedCount);
        Assert.Equal(0, first.FailedCount);
        Assert.Equal(0, second.ImportedCount);
        Assert.Equal(1, second.DuplicateCount);

        await using var store = new TrainingStore(_databasePath);
        await store.InitializeAsync();
        Assert.Single(await store.GetSessionsAsync());
    }

    [Fact]
    public void LabEngine_ConvertsPointerTrialsToObservations()
    {
        var capture = new LabSessionCapture
        {
            SessionId = "lab-engine",
            StartedUtc = DateTime.UtcNow.AddMinutes(-1),
            Trials =
            [
                new LabTrial
                {
                    Target = new LabTarget
                    {
                        Index = 0,
                        X = 200,
                        Y = 100,
                        Radius = 26
                    },
                    Start = new LabPoint(100, 100),
                    End = new LabPoint(220, 100),
                    StartMilliseconds = 0,
                    EndMilliseconds = 300,
                    Hit = false
                },
                new LabTrial
                {
                    Target = new LabTarget
                    {
                        Index = 1,
                        X = 100,
                        Y = 0,
                        Radius = 26
                    },
                    Start = new LabPoint(100, 100),
                    End = new LabPoint(100, 5),
                    StartMilliseconds = 300,
                    EndMilliseconds = 900,
                    Hit = true
                }
            ]
        };

        var document = new LabSessionEngine().BuildDocument(capture);

        Assert.Equal(ProviderRegistry.UamLab, document.Provider);
        Assert.Equal(LabSessionEngine.CaptureMethod, document.CaptureMethod);
        Assert.Equal("lab-engine", document.SourceSessionId);
        Assert.Equal(2, document.Observations.Count);

        var right = document.Observations[0];
        Assert.Equal(0, right.DirectionDegrees, 6);
        Assert.Equal(0.2, right.SignedErrorRatio, 6);
        Assert.Equal(300, right.AcquisitionTimeMs, 6);
        Assert.Equal(MovementKind.Flick, right.MovementKind);

        var up = document.Observations[1];
        Assert.Equal(90, up.DirectionDegrees, 6);
        Assert.Equal(MovementKind.Tracking, up.MovementKind);
        Assert.Contains("not first-person camera aiming", LabSessionEngine.Limitation);
    }

    [Fact]
    public async Task LabSession_ImportsAutomaticallyWithoutManualJson()
    {
        var service = new LabSessionService(_importService);
        var capture = new LabSessionCapture
        {
            SessionId = "lab-auto-import",
            StartedUtc = DateTime.UtcNow.AddMinutes(-1),
            Trials = Enumerable.Range(0, 3)
                .Select(index => new LabTrial
                {
                    Target = new LabTarget
                    {
                        Index = index,
                        X = 200 + index * 10,
                        Y = 100,
                        Radius = 26
                    },
                    Start = new LabPoint(100, 100),
                    End = new LabPoint(200 + index * 10, 100),
                    StartMilliseconds = index * 300,
                    EndMilliseconds = (index + 1) * 300,
                    Hit = true
                })
                .ToList()
        };

        var result = await service.CompleteAsync(capture);

        Assert.Equal(ImportOutcome.Imported, result.Import.Outcome);
        Assert.Equal(3, result.TrialCount);
        Assert.Equal(3, result.HitCount);
        Assert.Equal(1.0, result.HitRate, 6);

        await using var store = new TrainingStore(_databasePath);
        await store.InitializeAsync();
        var session = Assert.Single(await store.GetSessionsAsync());
        Assert.Equal("uam-lab", session.GameId);
        Assert.Equal(3, session.ObservationCount);
    }

    [Fact]
    public async Task LabViewModel_StartClickComplete_StoresASession()
    {
        var service = new LabSessionService(_importService);
        var vm = new LabViewModel(service);

        vm.StartSession(3);

        Assert.True(vm.IsRunning);
        Assert.True(vm.HasCurrentTarget);

        for (var index = 0; index < 3; index++)
        {
            vm.RecordClick(vm.CurrentTargetX, vm.CurrentTargetY);
        }

        Assert.Equal(3, vm.TrialCount);
        Assert.Equal(3, vm.HitCount);
        Assert.True(vm.CanComplete);

        await vm.CompleteSessionAsync();

        Assert.False(vm.HasError);
        Assert.NotNull(vm.LastResult);
        Assert.Equal(ImportOutcome.Imported, vm.LastResult!.Import.Outcome);
        Assert.Contains("Saved 3 trials", vm.StatusMessage);

        await using var store = new TrainingStore(_databasePath);
        await store.InitializeAsync();
        Assert.Single(await store.GetSessionsAsync());
    }

    [Fact]
    public async Task ProviderHealth_ReflectsRealImportsAndTerms()
    {
        await _importService.ImportContentAsync(
            TrainingDocumentJson.Serialize(Document("health-session", 1)));

        var health = new ProviderHealthService(_factory);
        var statuses = await health.GetStatusesAsync();

        var canonical = statuses.Single(x => x.Provider.Id == ProviderRegistry.CanonicalJson);
        var lab = statuses.Single(x => x.Provider.Id == ProviderRegistry.UamLab);
        var aimlabs = statuses.Single(x => x.Provider.Id == ProviderRegistry.Aimlabs);
        var riot = statuses.Single(x => x.Provider.Id == ProviderRegistry.RiotTelemetry);

        Assert.Equal(ProviderHealth.Connected, canonical.Health);
        Assert.Equal(ProviderHealth.Ready, lab.Health);
        Assert.Equal(ProviderHealth.NotConnected, aimlabs.Health);
        Assert.Equal(ProviderHealth.Blocked, riot.Health);
    }

    [Fact]
    public void Registry_RecordsTermsAndOnlyPermitsRealIntegrations()
    {
        Assert.True(ProviderRegistry.CanIntegrate(ProviderRegistry.UamLab));
        Assert.True(ProviderRegistry.CanIntegrate(ProviderRegistry.CanonicalJson));
        Assert.False(ProviderRegistry.CanIntegrate(ProviderRegistry.Aimlabs));
        Assert.False(ProviderRegistry.CanIntegrate(ProviderRegistry.Other));
        Assert.False(ProviderRegistry.CanIntegrate(ProviderRegistry.RiotTelemetry));

        var riot = ProviderRegistry.Get(ProviderRegistry.RiotTelemetry);
        Assert.Equal(TermsPosition.NotPermitted, riot.Terms);
        Assert.Equal(ProviderMaturity.Planned, riot.Maturity);

        var aimlabs = ProviderRegistry.Get(ProviderRegistry.Aimlabs);
        Assert.Equal(TermsPosition.PermissionRequested, aimlabs.Terms);
        Assert.Equal(ProviderMaturity.Research, aimlabs.Maturity);
    }

    [Fact]
    public void IntegrationsViewModel_ExposesTheRegistryAndLabWithoutBreakingTheLegacyList()
    {
        var vm = new IntegrationsViewModel();

        Assert.Equal(5, vm.Registry.Count);
        Assert.Contains(vm.Registry, x => x.Id == ProviderRegistry.UamLab);
        Assert.Contains(vm.Registry, x => x.Id == ProviderRegistry.RiotTelemetry && x.IsBlocked);
        Assert.NotNull(vm.Lab);
        Assert.Single(vm.Providers, x => x.IsAvailable);
    }

    [Fact]
    public async Task CompletedLabSession_AppearsInDashboardAndHistoryWithoutManualJson()
    {
        var lab = new LabSessionService(_importService);
        var capture = new LabSessionCapture
        {
            SessionId = "lab-end-to-end",
            StartedUtc = DateTime.UtcNow.AddMinutes(-1),
            Trials = Enumerable.Range(0, 4)
                .Select(index => new LabTrial
                {
                    Target = new LabTarget
                    {
                        Index = index,
                        X = 200 + index * 10,
                        Y = 100,
                        Radius = 26
                    },
                    Start = new LabPoint(100, 100),
                    End = new LabPoint(200 + index * 10, 100),
                    StartMilliseconds = index * 300,
                    EndMilliseconds = (index + 1) * 300,
                    Hit = true
                })
                .ToList()
        };

        var result = await lab.CompleteAsync(capture);
        Assert.Equal(ImportOutcome.Imported, result.Import.Outcome);

        var options = new UamApplicationOptions
        {
            DatabasePath = _databasePath,
            ApprovedBy = "import-lab-test"
        };

        var dashboard = new DashboardViewModel(
            new DashboardService(_factory, options));
        var history = new HistoryViewModel(new HistoryService(_factory));

        await dashboard.OnActivatedAsync();
        await history.OnActivatedAsync();

        Assert.True(dashboard.HasData);
        Assert.Equal("4", new string(dashboard.ObservationCountText.Where(char.IsDigit).ToArray()));
        Assert.Equal("1", dashboard.SessionCountText);
        Assert.Equal("1", dashboard.GameCountText);

        var row = Assert.Single(history.Sessions);
        Assert.Equal("lab-end-to-end", row.SessionId);
        Assert.Equal(4, row.ObservationCount);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // SQLite may briefly hold the file; temp output is disposable.
        }
    }
}
