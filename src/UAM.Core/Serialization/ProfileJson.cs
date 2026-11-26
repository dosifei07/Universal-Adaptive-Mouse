using System.Text.Json;
using System.Text.Json.Serialization;
using UAM.Core.Models;

namespace UAM.Core.Serialization;

/// <summary>
/// Single source of truth for profile serialization so the CLI, tests, and the
/// future desktop application produce identical files.
/// </summary>
public static class ProfileJson
{
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    public static string Serialize(GameProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return JsonSerializer.Serialize(profile, Options);
    }

    public static GameProfile Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        return JsonSerializer.Deserialize<GameProfile>(json, Options)
            ?? throw new JsonException("Profile JSON produced a null value.");
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        options.Converters.Add(
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));

        return options;
    }
}
