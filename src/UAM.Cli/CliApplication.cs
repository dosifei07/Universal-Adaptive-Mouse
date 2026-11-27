using System.Text.Json;
using UAM.AdaptiveEngine;
using UAM.Analytics;
using UAM.Application;
using UAM.Core.Models;
using UAM.Core.Serialization;
using UAM.Data;

namespace UAM.Cli;

internal static class CliApplication
{
    private const string DefaultDatabasePath = "uam.db";
    private const string DefaultPlayerId = "player-001";

    private static readonly HashSet<string> AnalyzeFlags =
        ["save", "approve", "help"];

    private static readonly HashSet<string> AnalyzeValues =
        ["game", "db", "export", "player"];

    public static async Task<int> RunAsync(
        string[] args,
        CancellationToken cancellationToken = default)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return 1;
        }

        if (args[0] is "--help" or "-h" or "help")
        {
            PrintUsage();
            return 0;
        }

        var command = args[0];
        var rest = args.Skip(1).ToArray();

        // Backwards-compatible shorthand: `UAM.Cli session.json`.
        if (!command.Equals("analyze", StringComparison.OrdinalIgnoreCase) &&
            !command.Equals("sessions", StringComparison.OrdinalIgnoreCase) &&
            !command.Equals("import", StringComparison.OrdinalIgnoreCase))
        {
            command = "analyze";
            rest = args;
        }

        try
        {
            return command.ToLowerInvariant() switch
            {
                "analyze" => await RunAnalyzeAsync(rest, cancellationToken),
                "sessions" => await RunSessionsAsync(rest, cancellationToken),
                "import" => await RunImportAsync(rest, cancellationToken),
                _ => 1
            };
        }
        catch (ProviderNotPermittedException exception)
        {
            Console.Error.WriteLine($"error: {exception.Message}");
            return 1;
        }
        catch (TrainingDocumentVersionException exception)
        {
            Console.Error.WriteLine($"error: {exception.Message}");
            return 1;
        }
        catch (CliUsageException exception)
        {
            Console.Error.WriteLine($"error: {exception.Message}");
            Console.Error.WriteLine("Run 'UAM.Cli --help' for usage.");
            return 1;
        }
        catch (JsonException exception)
        {
            Console.Error.WriteLine($"error: invalid training JSON: {exception.Message}");
            return 1;
        }
        catch (IOException exception)
        {
            Console.Error.WriteLine($"error: {exception.Message}");
            return 1;
        }
    }

    private static async Task<int> RunAnalyzeAsync(
        string[] args,
        CancellationToken cancellationToken)
    {
        if (args.Length == 0 || args[0] is "--help" or "-h")
        {
            PrintUsage();
            return args.Length == 0 ? 1 : 0;
        }

        var filePath = args[0];

        if (filePath.StartsWith("--", StringComparison.Ordinal))
        {
            throw new CliUsageException("analyze requires a training JSON file.");
        }

        var options = ParseOptions(args.Skip(1), AnalyzeFlags, AnalyzeValues);

        if (options.ContainsKey("help"))
        {
            PrintUsage();
            return 0;
        }

        if (!File.Exists(filePath))
        {
            Console.Error.WriteLine($"error: training file not found: {filePath}");
            return 1;
        }

        var json = await File.ReadAllTextAsync(filePath, cancellationToken);
        var observations = ObservationJson.DeserializeMany(json);

        if (observations.Count == 0)
        {
            Console.Error.WriteLine("error: no training observations found.");
            return 1;
        }

        var playerId = options.GetValueOrDefault("player") ?? DefaultPlayerId;

        var analyzer = new PerformanceAnalyzer();
        var analysis = analyzer.AnalyzeWithDiagnostics(playerId, observations);

        Console.WriteLine();
        Console.WriteLine("=== UAM ANALYSIS ===");
        Console.WriteLine($"Player:              {playerId}");
        Console.WriteLine($"Observations loaded: {observations.Count}");
        Console.WriteLine($"Accepted:            {analysis.AcceptedCount}");
        Console.WriteLine($"Rejected:            {analysis.RejectedCount}");

        if (analysis.RejectedCount > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Rejected observations:");

            foreach (var rejection in analysis.Rejections.Take(10))
            {
                Console.WriteLine(
                    $"  - {rejection.Observation.SessionId}/" +
                    $"{rejection.Observation.ScenarioId}: " +
                    $"{string.Join(", ", rejection.Validation.Codes)}");
            }

            if (analysis.RejectedCount > 10)
            {
                Console.WriteLine($"  ... and {analysis.RejectedCount - 10} more");
            }
        }

        if (analysis.AcceptedCount == 0)
        {
            Console.Error.WriteLine(
                "error: no valid observations remain after validation.");
            return 1;
        }

        var gameId = options.GetValueOrDefault("game")
            ?? observations[0].GameId;

        if (string.IsNullOrWhiteSpace(gameId))
        {
            throw new CliUsageException(
                "Could not determine a game id. Pass --game <id>.");
        }

        var gameObservations = observations
            .Where(x => x.GameId == gameId)
            .ToList();

        var gameModel = gameObservations.Count > 0
            ? analyzer.Analyze(playerId, gameObservations)
            : PlayerModel.Empty(playerId);

        Console.WriteLine($"Game:                {gameId}");
        Console.WriteLine(
            $"Game observations:   {gameModel.DirectionalEstimates.Sum(x => x.SampleCount)}");

        PrintDirectionalAnalysis(analysis.Model);
        PrintSpeedAnalysis(analysis.Model);

        var engine = new OptimizationEngine();

        var recommendation = engine.BuildRecommendation(
            gameId,
            analysis.Model,
            gameModel);

        PrintRecommendation(recommendation);

        if (options.ContainsKey("save"))
        {
            var databasePath = options.GetValueOrDefault("db")
                ?? DefaultDatabasePath;

            await using var store = new TrainingStore(databasePath);
            await store.InitializeAsync(cancellationToken);

            var saveResult = await store.SaveObservationsAsync(
                observations,
                cancellationToken);

            Console.WriteLine();
            Console.WriteLine($"Stored {saveResult.SavedCount} observation(s) in {databasePath}.");

            if (saveResult.RejectedCount > 0)
            {
                Console.WriteLine(
                    $"Skipped {saveResult.RejectedCount} invalid observation(s).");
            }
        }

        var exportPath = options.GetValueOrDefault("export");

        if (exportPath is null)
        {
            return 0;
        }

        if (!recommendation.IsActionable)
        {
            Console.Error.WriteLine(
                "error: recommendation is not actionable, so there is nothing to export.");
            return 2;
        }

        var approved = options.ContainsKey("approve");

        if (recommendation.RequiresApproval && !approved)
        {
            Console.Error.WriteLine(
                "error: this recommendation requires approval. " +
                "Review it, then re-run with --approve to export.");
            return 2;
        }

        var approver = new RecommendationApprover();
        var approvedProfile = approver.Approve(recommendation);

        var fullExportPath = Path.GetFullPath(exportPath);
        var exportDirectory = Path.GetDirectoryName(fullExportPath);

        if (!string.IsNullOrEmpty(exportDirectory))
        {
            Directory.CreateDirectory(exportDirectory);
        }

        await File.WriteAllTextAsync(
            fullExportPath,
            ProfileJson.Serialize(approvedProfile),
            cancellationToken);

        Console.WriteLine();
        Console.WriteLine($"Exported approved profile: {fullExportPath}");

        return 0;
    }

    private static async Task<int> RunImportAsync(
        string[] args,
        CancellationToken cancellationToken)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return 1;
        }

        string? filePath;

        if (args[0].StartsWith("--", StringComparison.Ordinal))
        {
            filePath = null;
        }
        else
        {
            filePath = args[0];
            args = args.Skip(1).ToArray();
        }

        var options = ParseOptions(args, ["help"], ["db", "folder"]);

        if (options.ContainsKey("help"))
        {
            PrintUsage();
            return 0;
        }

        var databasePath = options.GetValueOrDefault("db")
            ?? DefaultDatabasePath;

        var applicationOptions = new UamApplicationOptions
        {
            DatabasePath = databasePath
        };

        var importService = new ImportService(
            new TrainingStoreFactory(applicationOptions.DatabasePath));

        var folder = options.GetValueOrDefault("folder");

        if (folder is not null)
        {
            if (filePath is not null)
            {
                throw new CliUsageException(
                    "import accepts either a file or --folder, not both.");
            }

            await using var watcher = new WatchedFolderImportService(
                importService,
                folder);

            var scan = await watcher.ScanOnceAsync(cancellationToken);

            Console.WriteLine();
            Console.WriteLine($"=== IMPORT SCAN ({Path.GetFullPath(folder)}) ===");
            Console.WriteLine($"Imported:   {scan.ImportedCount}");
            Console.WriteLine($"Duplicates: {scan.DuplicateCount}");
            Console.WriteLine($"Rejected:   {scan.RejectedCount}");
            Console.WriteLine($"Failed:     {scan.FailedCount}");

            foreach (var message in scan.Messages)
            {
                Console.Error.WriteLine($"  - {message}");
            }

            return scan.FailedCount > 0 ? 1 : 0;
        }

        if (filePath is null)
        {
            throw new CliUsageException(
                "import requires a training document file or --folder <path>.");
        }

        if (!File.Exists(filePath))
        {
            Console.Error.WriteLine($"error: training document not found: {filePath}");
            return 1;
        }

        var result = await importService.ImportFileAsync(
            filePath,
            cancellationToken);

        Console.WriteLine();
        Console.WriteLine("=== IMPORT ===");
        Console.WriteLine($"Provider:    {result.Document.Provider}");
        Console.WriteLine($"Session:     {result.Document.SourceSessionId}");
        Console.WriteLine($"Capture:     {result.Document.CaptureMethod}");
        Console.WriteLine($"Outcome:     {result.Outcome}");
        Console.WriteLine($"Stored:      {result.SavedCount}");
        Console.WriteLine($"Rejected:    {result.RejectedCount}");
        Console.WriteLine(result.Detail);

        return result.Outcome == ImportOutcome.Rejected ? 1 : 0;
    }

    private static async Task<int> RunSessionsAsync(
        string[] args,
        CancellationToken cancellationToken)
    {
        var options = ParseOptions(args, ["help"], ["db"]);

        if (options.ContainsKey("help"))
        {
            PrintUsage();
            return 0;
        }

        var databasePath = options.GetValueOrDefault("db")
            ?? DefaultDatabasePath;

        await using var store = new TrainingStore(databasePath);
        await store.InitializeAsync(cancellationToken);

        var sessions = await store.GetSessionsAsync(cancellationToken);

        Console.WriteLine();
        Console.WriteLine($"=== STORED SESSIONS ({databasePath}) ===");

        if (sessions.Count == 0)
        {
            Console.WriteLine("No sessions stored yet. Run analyze with --save.");
            return 0;
        }

        foreach (var session in sessions)
        {
            Console.WriteLine(
                $"{session.SessionId} | {session.GameId} | " +
                $"{session.ScenarioId} | {session.ObservationCount} obs | " +
                $"{session.LastObservationUtc:u}");
        }

        return 0;
    }

    private static void PrintDirectionalAnalysis(PlayerModel model)
    {
        Console.WriteLine();
        Console.WriteLine("Directional analysis (global model):");

        if (model.DirectionalEstimates.Count == 0)
        {
            Console.WriteLine("  (no data)");
            return;
        }

        foreach (var estimate in model.DirectionalEstimates)
        {
            Console.WriteLine(
                $"  Sector {estimate.Sector}: " +
                $"Error={estimate.MeanErrorRatio:+0.0000;-0.0000;0.0000}, " +
                $"Samples={estimate.SampleCount}, " +
                $"Confidence={estimate.Confidence:P0}");
        }
    }

    private static void PrintSpeedAnalysis(PlayerModel model)
    {
        Console.WriteLine();
        Console.WriteLine("Speed analysis (global model):");

        if (model.SpeedEstimates.Count == 0)
        {
            Console.WriteLine("  (no data)");
            return;
        }

        foreach (var estimate in model.SpeedEstimates)
        {
            Console.WriteLine(
                $"  {estimate.Band,-6}: " +
                $"Error={estimate.MeanErrorRatio:+0.0000;-0.0000;0.0000}, " +
                $"Samples={estimate.SampleCount}, " +
                $"Confidence={estimate.Confidence:P0}");
        }
    }

    private static void PrintRecommendation(
        OptimizationRecommendation recommendation)
    {
        Console.WriteLine();
        Console.WriteLine("=== RECOMMENDATION ===");

        foreach (var gain in recommendation.ProposedProfile.DirectionalGains
                     .OrderBy(x => x.Key))
        {
            Console.WriteLine($"  Sector {gain.Key}: {gain.Value:F4}x");
        }

        foreach (var gain in recommendation.ProposedProfile.SpeedGains
                     .OrderBy(x => x.Key))
        {
            Console.WriteLine($"  {gain.Key,-6}: {gain.Value:F4}x");
        }

        Console.WriteLine();
        Console.WriteLine($"Actionable:        {recommendation.IsActionable}");
        Console.WriteLine($"Approval required: {recommendation.RequiresApproval}");
        Console.WriteLine($"Confidence:        {recommendation.Confidence:P0}");
        Console.WriteLine($"Reason:            {recommendation.Reason}");
    }

    private static Dictionary<string, string?> ParseOptions(
        IEnumerable<string> args,
        HashSet<string> flags,
        HashSet<string> valued)
    {
        var options = new Dictionary<string, string?>(
            StringComparer.OrdinalIgnoreCase);

        var tokens = args.ToArray();

        for (var index = 0; index < tokens.Length; index++)
        {
            var token = tokens[index];

            if (!token.StartsWith("--", StringComparison.Ordinal) ||
                token.Length <= 2)
            {
                throw new CliUsageException($"unexpected argument: {token}");
            }

            var name = token[2..];
            var equalsIndex = name.IndexOf('=');
            string? inlineValue = null;

            if (equalsIndex >= 0)
            {
                inlineValue = name[(equalsIndex + 1)..];
                name = name[..equalsIndex];
            }

            if (flags.Contains(name))
            {
                if (inlineValue is not null)
                {
                    throw new CliUsageException(
                        $"--{name} does not accept a value.");
                }

                if (!options.TryAdd(name, null))
                {
                    throw new CliUsageException($"duplicate option: --{name}");
                }

                continue;
            }

            if (!valued.Contains(name))
            {
                throw new CliUsageException($"unknown option: --{name}");
            }

            var value = inlineValue;

            if (value is null)
            {
                if (index + 1 >= tokens.Length ||
                    tokens[index + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    throw new CliUsageException(
                        $"--{name} requires a value.");
                }

                value = tokens[++index];
            }

            if (string.IsNullOrWhiteSpace(value))
            {
                throw new CliUsageException($"--{name} requires a value.");
            }

            if (!options.TryAdd(name, value))
            {
                throw new CliUsageException($"duplicate option: --{name}");
            }
        }

        return options;
    }

    private static void PrintUsage()
    {
        Console.WriteLine(
            """
            UAM Core v0.1

            Usage:
              UAM.Cli analyze <training-session.json> [options]
              UAM.Cli import <training-document.json> [--db <path>]
              UAM.Cli import --folder <path> [--db <path>]
              UAM.Cli sessions [--db <path>]

            Analyze options:
              --game <id>       Game to profile (default: first observation's game)
              --player <id>     Player id (default: player-001)
              --db <path>       SQLite database path (default: uam.db)
              --save            Store accepted observations locally
              --export <path>   Export the approved profile as JSON
              --approve         Explicitly approve the recommendation for export
              --help            Show this help

            Session options:
              --db <path>       SQLite database path (default: uam.db)

            Import options:
              --db <path>       SQLite database path (default: uam.db)
              --folder <path>   Import every *.json document in a folder

            Examples:
              UAM.Cli analyze samples/training-session.json
              UAM.Cli analyze samples/training-session.json --save
              UAM.Cli analyze data.json --export out/profile.json --approve
              UAM.Cli import samples/training-document.json
              UAM.Cli import --folder imports
              UAM.Cli sessions
            """);
    }
}

internal sealed class CliUsageException(string message) : Exception(message);
