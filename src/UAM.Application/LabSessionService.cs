using UAM.Core.Serialization;

namespace UAM.Application;

public interface ILabSessionService
{
    /// <summary>
    /// Converts and imports a completed lab session. The session appears in
    /// UAM immediately; no manual JSON handling is involved.
    /// </summary>
    Task<LabSessionResult> CompleteAsync(
        LabSessionCapture capture,
        CancellationToken cancellationToken = default);
}

public sealed class LabSessionService : ILabSessionService
{
    private readonly LabSessionEngine _engine;
    private readonly IImportService _importService;

    public LabSessionService(
        LabSessionEngine engine,
        IImportService importService)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _importService = importService
            ?? throw new ArgumentNullException(nameof(importService));
    }

    public LabSessionService(IImportService importService)
        : this(new LabSessionEngine(), importService)
    {
    }

    public async Task<LabSessionResult> CompleteAsync(
        LabSessionCapture capture,
        CancellationToken cancellationToken = default)
    {
        var document = _engine.BuildDocument(capture);
        var content = TrainingDocumentJson.Serialize(document);
        var hash = TrainingDocumentJson.ComputeContentHash(content);

        var import = await _importService.ImportDocumentAsync(
            document,
            hash,
            cancellationToken);

        return new LabSessionResult
        {
            Document = document,
            Import = import,
            TrialCount = capture.Trials.Count,
            HitCount = capture.Trials.Count(x => x.Hit)
        };
    }
}
