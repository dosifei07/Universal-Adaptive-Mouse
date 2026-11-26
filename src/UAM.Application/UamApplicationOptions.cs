namespace UAM.Application;

/// <summary>
/// Configuration shared by every application service. The desktop app and the
/// CLI use the same defaults so they read and write the same local database.
/// </summary>
public sealed record UamApplicationOptions
{
    public const string DefaultDatabasePath = "uam.db";

    public const string DefaultPlayerId = "player-001";

    public string DatabasePath { get; init; } = DefaultDatabasePath;

    public string PlayerId { get; init; } = DefaultPlayerId;

    public string ApprovedBy { get; init; } = "local-user";
}
