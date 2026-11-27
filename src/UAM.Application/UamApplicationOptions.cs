namespace UAM.Application;

/// <summary>
/// Configuration shared by every application service. The desktop app and the
/// CLI use the same defaults so they read and write the same local database.
/// </summary>
public sealed record UamApplicationOptions
{
    public const string DefaultDatabasePath = "uam.db";

    public const string DefaultPlayerId = "player-001";

    public const string DefaultImportFolderPath = "imports";

    public string DatabasePath { get; init; } = DefaultDatabasePath;

    public string PlayerId { get; init; } = DefaultPlayerId;

    public string ApprovedBy { get; init; } = "local-user";

    /// <summary>Folder scanned for canonical training documents.</summary>
    public string ImportFolderPath { get; init; } = DefaultImportFolderPath;

    /// <summary>
    /// When true the desktop app watches <see cref="ImportFolderPath"/> and
    /// imports new documents automatically. Re-scans are safe because import
    /// identity makes duplicates harmless.
    /// </summary>
    public bool WatchImports { get; init; } = true;
}
