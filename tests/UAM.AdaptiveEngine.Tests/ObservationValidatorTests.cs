using UAM.Core.Models;
using UAM.Core.Validation;

namespace UAM.AdaptiveEngine.Tests;

public sealed class ObservationValidatorTests
{
    private static ObservationValidator CreateValidator(
        ObservationValidationOptions? options = null,
        DateTime? now = null)
    {
        return new ObservationValidator(
            options,
            () => now ?? TestData.Now);
    }

    [Fact]
    public void ValidObservation_IsAccepted()
    {
        var validator = CreateValidator();

        var result = validator.Validate(TestData.Observation());

        Assert.True(result.IsValid);
        Assert.Empty(result.Codes);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void NonFiniteDirection_IsRejected(double direction)
    {
        var validator = CreateValidator();
        var observation = TestData.Observation() with
        {
            DirectionDegrees = direction
        };

        var result = validator.Validate(observation);

        Assert.Contains(ObservationValidationCode.NonFiniteDirection, result.Codes);
    }

    [Theory]
    [InlineData(360.01)]
    [InlineData(-360.01)]
    [InlineData(720)]
    public void DirectionOutsideRange_IsRejected(double direction)
    {
        var validator = CreateValidator();
        var observation = TestData.Observation() with
        {
            DirectionDegrees = direction
        };

        var result = validator.Validate(observation);

        Assert.Contains(ObservationValidationCode.DirectionOutOfRange, result.Codes);
    }

    [Fact]
    public void DirectionAtBoundary_IsAccepted()
    {
        var validator = CreateValidator();

        Assert.True(validator.Validate(
            TestData.Observation() with { DirectionDegrees = 360 }).IsValid);
        Assert.True(validator.Validate(
            TestData.Observation() with { DirectionDegrees = -360 }).IsValid);
    }

    [Fact]
    public void NonFiniteValues_AreRejected()
    {
        var validator = CreateValidator();

        var speed = validator.Validate(TestData.Observation() with
        {
            SpeedPixelsPerSecond = double.NaN
        });
        var error = validator.Validate(TestData.Observation() with
        {
            SignedErrorRatio = double.PositiveInfinity
        });
        var acquisition = validator.Validate(TestData.Observation() with
        {
            AcquisitionTimeMs = double.NaN
        });

        Assert.Contains(ObservationValidationCode.NonFiniteSpeed, speed.Codes);
        Assert.Contains(ObservationValidationCode.NonFiniteSignedError, error.Codes);
        Assert.Contains(
            ObservationValidationCode.NonFiniteAcquisitionTime,
            acquisition.Codes);
    }

    [Fact]
    public void OutOfRangeValues_AreRejected()
    {
        var validator = CreateValidator();

        var negativeSpeed = validator.Validate(TestData.Observation() with
        {
            SpeedPixelsPerSecond = -1
        });
        var extremeSpeed = validator.Validate(TestData.Observation() with
        {
            SpeedPixelsPerSecond = 100_001
        });
        var extremeError = validator.Validate(TestData.Observation() with
        {
            SignedErrorRatio = 2.01
        });
        var negativeTime = validator.Validate(TestData.Observation() with
        {
            AcquisitionTimeMs = -1
        });
        var extremeTime = validator.Validate(TestData.Observation() with
        {
            AcquisitionTimeMs = 60_001
        });

        Assert.Contains(ObservationValidationCode.NegativeSpeed, negativeSpeed.Codes);
        Assert.Contains(ObservationValidationCode.SpeedOutlier, extremeSpeed.Codes);
        Assert.Contains(
            ObservationValidationCode.SignedErrorOutlier,
            extremeError.Codes);
        Assert.Contains(
            ObservationValidationCode.NegativeAcquisitionTime,
            negativeTime.Codes);
        Assert.Contains(
            ObservationValidationCode.AcquisitionTimeOutlier,
            extremeTime.Codes);
    }

    [Fact]
    public void ImpossibleTimestamps_AreRejected()
    {
        var now = TestData.Now;
        var validator = CreateValidator(now: now);

        var tooOld = validator.Validate(TestData.Observation() with
        {
            TimestampUtc = new DateTime(1999, 12, 31, 23, 59, 59, DateTimeKind.Utc)
        });
        var inFuture = validator.Validate(TestData.Observation() with
        {
            TimestampUtc = now.AddHours(25)
        });

        Assert.Contains(ObservationValidationCode.TimestampTooOld, tooOld.Codes);
        Assert.Contains(ObservationValidationCode.TimestampInFuture, inFuture.Codes);
    }

    [Fact]
    public void TimestampWithinAllowedSkew_IsAccepted()
    {
        var now = TestData.Now;
        var validator = CreateValidator(now: now);

        var result = validator.Validate(TestData.Observation() with
        {
            TimestampUtc = now.AddHours(23)
        });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void EmptyIdentifiers_AreRejected()
    {
        var validator = CreateValidator();
        var observation = TestData.Observation() with
        {
            SessionId = " ",
            GameId = "",
            ScenarioId = ""
        };

        var result = validator.Validate(observation);

        Assert.Contains(ObservationValidationCode.MissingSessionId, result.Codes);
        Assert.Contains(ObservationValidationCode.MissingGameId, result.Codes);
        Assert.Contains(ObservationValidationCode.MissingScenarioId, result.Codes);
    }

    [Fact]
    public void UndefinedMovementKind_IsRejected()
    {
        var validator = CreateValidator();
        var observation = TestData.Observation() with
        {
            MovementKind = (MovementKind)99
        };

        var result = validator.Validate(observation);

        Assert.Contains(
            ObservationValidationCode.UndefinedMovementKind,
            result.Codes);
    }

    [Fact]
    public void MultipleProblems_AreAllReported()
    {
        var validator = CreateValidator();
        var observation = TestData.Observation() with
        {
            GameId = "",
            DirectionDegrees = double.NaN,
            SpeedPixelsPerSecond = -5,
            SignedErrorRatio = 5,
            AcquisitionTimeMs = -10,
            MovementKind = (MovementKind)42
        };

        var result = validator.Validate(observation);

        Assert.False(result.IsValid);
        Assert.True(result.Codes.Count >= 6);
    }
}
