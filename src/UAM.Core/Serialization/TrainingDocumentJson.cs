using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using UAM.Core.Models;

namespace UAM.Core.Serialization;

/// <summary>Thrown when a document declares an unsupported schema version.</summary>
public sealed class TrainingDocumentVersionException : Exception
{
    public TrainingDocumentVersionException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// Reads and writes the canonical, versioned training document. A document is
/// a JSON object with provenance plus an observations array. For backwards
/// compatibility a bare observation array is still accepted and wrapped in a
/// canonical envelope.
/// </summary>
public static class TrainingDocumentJson
{
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    public static TrainingDocument Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        using var parsed = JsonDocument.Parse(json);

        if (parsed.RootElement.ValueKind == JsonValueKind.Array)
        {
            return FromLegacyArray(json, parsed.RootElement);
        }

        if (parsed.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException(
                "A training document must be a JSON object or a legacy " +
                "observation array.");
        }

        var document = JsonSerializer.Deserialize<TrainingDocument>(json, Options)
            ?? throw new JsonException("The training document was empty.");

        Validate(document);
        return document;
    }

    public static bool TryDeserialize(
        string json,
        out TrainingDocument? document,
        out string? error)
    {
        try
        {
            document = Deserialize(json);
            error = null;
            return true;
        }
        catch (Exception exception) when (
            exception is JsonException or
            TrainingDocumentVersionException or
            ArgumentException)
        {
            document = null;
            error = exception.Message;
            return false;
        }
    }

    public static string Serialize(TrainingDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        Validate(document);
        return JsonSerializer.Serialize(document, Options);
    }

    /// <summary>
    /// Lower-case SHA-256 of the exact document bytes. The hash is part of the
    /// import identity, so white-space changes count as a different document.
    /// </summary>
    public static string ComputeContentHash(string content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexStringLower(bytes);
    }

    private static TrainingDocument FromLegacyArray(
        string json,
        JsonElement root)
    {
        var observations = JsonSerializer.Deserialize<List<TrainingObservation>>(
            root.GetRawText(),
            Options) ?? [];

        if (observations.Count == 0)
        {
            throw new JsonException("No training observations were found.");
        }

        var sourceSessionId = observations
            .Select(x => x.SessionId)
            .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))
            ?? "legacy-" + ComputeContentHash(json)[..12];

        return new TrainingDocument
        {
            SchemaVersion = TrainingDocumentFormat.CurrentSchemaVersion,
            Provider = ProviderRegistry.CanonicalJson,
            CaptureMethod = "legacy-json",
            SourceSessionId = sourceSessionId,
            Observations = observations
        };
    }

    private static void Validate(TrainingDocument document)
    {
        if (string.IsNullOrWhiteSpace(document.SchemaVersion))
        {
            throw new JsonException("The document has no schema version.");
        }

        var major = ParseMajor(document.SchemaVersion);

        if (major > TrainingDocumentFormat.SupportedMajorVersion)
        {
            throw new TrainingDocumentVersionException(
                $"Schema version '{document.SchemaVersion}' is newer than this " +
                $"build supports (major {TrainingDocumentFormat.SupportedMajorVersion}).");
        }

        if (major < 1)
        {
            throw new TrainingDocumentVersionException(
                $"Schema version '{document.SchemaVersion}' is not supported.");
        }

        if (string.IsNullOrWhiteSpace(document.Provider))
        {
            throw new JsonException("The document has no provider.");
        }

        if (string.IsNullOrWhiteSpace(document.CaptureMethod))
        {
            throw new JsonException("The document has no capture method.");
        }

        if (string.IsNullOrWhiteSpace(document.SourceSessionId))
        {
            throw new JsonException("The document has no source session id.");
        }

        if (document.Observations is null || document.Observations.Count == 0)
        {
            throw new JsonException("The document contains no observations.");
        }
    }

    private static int ParseMajor(string version)
    {
        var majorText = version.Split('.', 2)[0];

        if (!int.TryParse(majorText, out var major))
        {
            throw new TrainingDocumentVersionException(
                $"Schema version '{version}' is not a valid version.");
        }

        return major;
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        options.Converters.Add(
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));

        return options;
    }
}
