using System.Text.Json;
using System.Text.Json.Serialization;
using UAM.Core.Models;

namespace UAM.Core.Serialization;

/// <summary>
/// Canonical JSON format for training observations. Providers should emit
/// camelCase properties; enum values may be strings or integers on input.
/// </summary>
public static class ObservationJson
{
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    public static IReadOnlyList<TrainingObservation> DeserializeMany(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        return JsonSerializer.Deserialize<List<TrainingObservation>>(json, Options)
            ?? [];
    }

    public static string SerializeMany(IEnumerable<TrainingObservation> observations)
    {
        ArgumentNullException.ThrowIfNull(observations);
        return JsonSerializer.Serialize(observations.ToList(), Options);
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
