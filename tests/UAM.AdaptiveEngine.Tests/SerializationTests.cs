using System.Text.Json;
using UAM.Core.Models;
using UAM.Core.Serialization;

namespace UAM.AdaptiveEngine.Tests;

public sealed class SerializationTests
{
    private static string SamplePath(string fileName)
    {
        return Path.Combine(AppContext.BaseDirectory, "samples", fileName);
    }

    [Fact]
    public void SampleTrainingSession_Deserializes()
    {
        var json = File.ReadAllText(SamplePath("training-session.json"));

        var observations = ObservationJson.DeserializeMany(json);

        Assert.Equal(2, observations.Count);

        var first = observations[0];
        Assert.Equal("session-001", first.SessionId);
        Assert.Equal("valorant", first.GameId);
        Assert.Equal("sixshot", first.ScenarioId);
        Assert.Equal(0, first.DirectionDegrees);
        Assert.Equal(650, first.SpeedPixelsPerSecond);
        Assert.Equal(0.12, first.SignedErrorRatio, 10);
        Assert.Equal(420, first.AcquisitionTimeMs);
        Assert.False(first.Hit);
        Assert.Equal(MovementKind.Flick, first.MovementKind);

        Assert.True(observations[1].Hit);
        Assert.Equal(-0.08, observations[1].SignedErrorRatio, 10);
    }

    [Fact]
    public void ExtendedSample_DeserializesAllObservations()
    {
        var json = File.ReadAllText(
            SamplePath("training-session-extended.json"));

        var observations = ObservationJson.DeserializeMany(json);

        Assert.Equal(80, observations.Count);
        Assert.All(observations, x => Assert.Equal("valorant", x.GameId));
    }

    [Fact]
    public void ObservationJson_RoundTrips()
    {
        var original = TestData.Observation(
            sector: 3,
            errorRatio: -0.25,
            speed: 1234.5,
            movementKind: MovementKind.Tracking,
            hit: true);

        var json = ObservationJson.SerializeMany([original]);
        var roundTripped = ObservationJson.DeserializeMany(json).Single();

        Assert.Equal(original, roundTripped);
        Assert.Contains("\"movementKind\": \"tracking\"", json);
    }

    [Fact]
    public void ObservationJson_AcceptsStringEnums()
    {
        var json =
            """
            [{
              "sessionId": "s",
              "gameId": "g",
              "scenarioId": "c",
              "timestampUtc": "2026-11-26T12:00:00Z",
              "directionDegrees": 90,
              "speedPixelsPerSecond": 500,
              "signedErrorRatio": 0.1,
              "acquisitionTimeMs": 300,
              "hit": true,
              "movementKind": "microCorrection"
            }]
            """;

        var observation = ObservationJson.DeserializeMany(json).Single();

        Assert.Equal(MovementKind.MicroCorrection, observation.MovementKind);
    }

    [Fact]
    public void ObservationJson_MissingRequiredPropertyThrows()
    {
        var json =
            """
            [{
              "gameId": "g",
              "scenarioId": "c",
              "timestampUtc": "2026-11-26T12:00:00Z",
              "directionDegrees": 90,
              "speedPixelsPerSecond": 500,
              "signedErrorRatio": 0.1,
              "acquisitionTimeMs": 300,
              "hit": true,
              "movementKind": 0
            }]
            """;

        Assert.Throws<JsonException>(() =>
            ObservationJson.DeserializeMany(json));
    }

    [Fact]
    public void ProfileJson_RoundTrips()
    {
        var profile = new GameProfile
        {
            GameId = "valorant",
            BaseSensitivity = 1.0,
            Dpi = 800,
            InGameSensitivity = 0.35,
            DirectionalGains = Enumerable.Range(0, 8)
                .ToDictionary(x => x, x => 1.0 + (x * 0.001)),
            SpeedGains = Enum.GetValues<SpeedBand>()
                .ToDictionary(x => x, x => 1.0),
            UpdatedAtUtc = TestData.BaseTimestamp
        };

        var json = ProfileJson.Serialize(profile);
        var roundTripped = ProfileJson.Deserialize(json);

        Assert.Equal(profile.GameId, roundTripped.GameId);
        Assert.Equal(profile.Dpi, roundTripped.Dpi);
        Assert.Equal(
            profile.InGameSensitivity,
            roundTripped.InGameSensitivity);
        Assert.Equal(profile.UpdatedAtUtc, roundTripped.UpdatedAtUtc);

        Assert.Equal(
            profile.DirectionalGains.Count,
            roundTripped.DirectionalGains.Count);

        foreach (var (sector, gain) in profile.DirectionalGains)
        {
            Assert.Equal(gain, roundTripped.DirectionalGains[sector], 10);
        }

        foreach (var (band, gain) in profile.SpeedGains)
        {
            Assert.Equal(gain, roundTripped.SpeedGains[band], 10);
        }

        Assert.Contains("\"directionalGains\"", json);
        Assert.Contains("\"slow\"", json);
    }

    [Fact]
    public void ProfileJson_ContainsExportedMetadata()
    {
        var profile = new GameProfile
        {
            GameId = "Aim Labs / Test",
            DirectionalGains = new Dictionary<int, double>(),
            SpeedGains = new Dictionary<SpeedBand, double>(),
            UpdatedAtUtc = new DateTime(2026, 11, 26, 15, 0, 0, DateTimeKind.Utc)
        };

        Assert.Equal(
            "uam-profile-Aim-Labs---Test-20261126T150000Z",
            profile.ToFileIdentifier());
    }
}
