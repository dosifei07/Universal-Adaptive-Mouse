using UAM.Core.Models;
using UAM.Data;

namespace UAM.AdaptiveEngine.Tests;

public sealed class TrainingStoreTests : IDisposable
{
    private readonly string _directory;
    private readonly string _databasePath;

    public TrainingStoreTests()
    {
        _directory = Path.Combine(
            Path.GetTempPath(),
            "uam-tests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(_directory);
        _databasePath = Path.Combine(_directory, "test.db");
    }

    [Fact]
    public async Task SaveAndLoad_RoundTripsObservations()
    {
        var observations = new[]
        {
            TestData.Observation(
                sector: 0,
                errorRatio: 0.12,
                speed: 650,
                timestampUtc: TestData.BaseTimestamp,
                hit: false),
            TestData.Observation(
                sector: 4,
                errorRatio: -0.08,
                speed: 500,
                timestampUtc: TestData.BaseTimestamp.AddSeconds(1),
                movementKind: MovementKind.Tracking,
                hit: true)
        };

        await using (var store = new TrainingStore(
                         _databasePath,
                         TestData.CreateValidator()))
        {
            await store.InitializeAsync();

            var saveResult = await store.SaveObservationsAsync(observations);

            Assert.Equal(2, saveResult.SavedCount);
            Assert.Equal(0, saveResult.RejectedCount);

            var loaded = await store.LoadObservationsAsync();

            Assert.Equal(2, loaded.Count);

            Assert.Equal(observations[0].SessionId, loaded[0].SessionId);
            Assert.Equal(observations[0].GameId, loaded[0].GameId);
            Assert.Equal(observations[0].ScenarioId, loaded[0].ScenarioId);
            Assert.Equal(
                observations[0].TimestampUtc,
                loaded[0].TimestampUtc);
            Assert.Equal(observations[0].DirectionDegrees, loaded[0].DirectionDegrees);
            Assert.Equal(
                observations[0].SpeedPixelsPerSecond,
                loaded[0].SpeedPixelsPerSecond);
            Assert.Equal(
                observations[0].SignedErrorRatio,
                loaded[0].SignedErrorRatio);
            Assert.Equal(
                observations[0].AcquisitionTimeMs,
                loaded[0].AcquisitionTimeMs);
            Assert.Equal(observations[0].Hit, loaded[0].Hit);
            Assert.Equal(
                observations[0].MovementKind,
                loaded[0].MovementKind);
            Assert.Equal(
                MovementKind.Tracking,
                loaded[1].MovementKind);
        }

        // Verify the data survives a fresh context.
        await using (var reopened = new TrainingStore(
                         _databasePath,
                         TestData.CreateValidator()))
        {
            await reopened.InitializeAsync();
            var loaded = await reopened.LoadObservationsAsync();

            Assert.Equal(2, loaded.Count);
        }
    }

    [Fact]
    public async Task SaveObservations_RejectsInvalidRows()
    {
        await using var store = new TrainingStore(
            _databasePath,
            TestData.CreateValidator());

        await store.InitializeAsync();

        var observations = new[]
        {
            TestData.Observation(),
            TestData.Observation() with { SignedErrorRatio = double.NaN },
            TestData.Observation() with { SpeedPixelsPerSecond = -100 }
        };

        var result = await store.SaveObservationsAsync(observations);

        Assert.Equal(1, result.SavedCount);
        Assert.Equal(2, result.RejectedCount);
        Assert.Equal(2, result.Rejections.Count);

        var loaded = await store.LoadObservationsAsync();
        Assert.Single(loaded);
    }

    [Fact]
    public async Task GetSessions_SummarizesStoredSessions()
    {
        await using var store = new TrainingStore(
            _databasePath,
            TestData.CreateValidator());

        await store.InitializeAsync();

        var observations = new[]
        {
            TestData.Observation(
                sessionId: "session-a",
                timestampUtc: TestData.BaseTimestamp),
            TestData.Observation(
                sessionId: "session-a",
                timestampUtc: TestData.BaseTimestamp.AddSeconds(2)),
            TestData.Observation(
                sessionId: "session-b",
                gameId: "cs2",
                scenarioId: "aim-botz",
                timestampUtc: TestData.BaseTimestamp.AddSeconds(5))
        };

        await store.SaveObservationsAsync(observations);

        var sessions = await store.GetSessionsAsync();

        Assert.Equal(2, sessions.Count);

        var sessionA = Assert.Single(
            sessions,
            x => x.SessionId == "session-a");

        Assert.Equal("valorant", sessionA.GameId);
        Assert.Equal(2, sessionA.ObservationCount);
        Assert.Equal(TestData.BaseTimestamp, sessionA.FirstObservationUtc);
        Assert.Equal(
            TestData.BaseTimestamp.AddSeconds(2),
            sessionA.LastObservationUtc);

        var valorantOnly = await store.LoadObservationsAsync("valorant");
        var cs2Only = await store.LoadObservationsAsync("cs2");

        Assert.Equal(2, valorantOnly.Count);
        Assert.Single(cs2Only);
    }

    [Fact]
    public async Task SavingSameSessionTwice_UpdatesSummary()
    {
        var observations = TestData.Repeat(3);

        await using var store = new TrainingStore(
            _databasePath,
            TestData.CreateValidator());

        await store.InitializeAsync();

        await store.SaveObservationsAsync(observations);
        await store.SaveObservationsAsync(observations);

        var session = Assert.Single(await store.GetSessionsAsync());
        Assert.Equal(6, session.ObservationCount);

        var loaded = await store.LoadObservationsAsync();
        Assert.Equal(6, loaded.Count);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
