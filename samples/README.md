# Sample data

## `training-session.json`

Two synthetic observations. This exists to verify JSON serialization and the
insufficient-data gate. It is not real Aimlabs telemetry and cannot produce an
actionable recommendation.

## `training-session-extended.json`

80 synthetic observations: ten per direction sector, spread across the three
speed bands. Sectors 0 (right) and 4 (left) deliberately contain a consistent
overshoot and undershoot so the recommendation path is exercised.

This is still synthetic, generated for pipeline testing. It is not evidence that
the proposed gains improve aim.

Both files use the canonical observation format:

```json
{
  "sessionId": "session-001",
  "gameId": "valorant",
  "scenarioId": "sixshot",
  "timestampUtc": "2026-11-26T12:00:00Z",
  "directionDegrees": 0,
  "speedPixelsPerSecond": 650,
  "signedErrorRatio": 0.12,
  "acquisitionTimeMs": 420,
  "hit": false,
  "movementKind": "flick"
}
```

`movementKind` may be a string (`flick`, `tracking`, `microCorrection`) or its
integer value. `signedErrorRatio` is positive for overshoot and negative for
undershoot.
