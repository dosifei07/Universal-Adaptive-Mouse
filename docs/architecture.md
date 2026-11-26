# Architecture

## Dependency direction

```text
                    ┌───────────────┐
                    │   UAM.Core    │
                    │ models        │
                    │ validation    │
                    │ JSON formats  │
                    └───────┬───────┘
                            │
          ┌─────────────────┼──────────────────┐
          │                 │                  │
  ┌───────▼────────┐ ┌──────▼─────────┐ ┌──────▼──────┐
  │ UAM.Analytics  │ │  UAM.Data      │ │ future      │
  │ performance    │ │  EF Core +     │ │ input layer │
  │ model          │ │  SQLite        │ │             │
  └───────┬────────┘ └──────┬─────────┘ └─────────────┘
          │                 │
  ┌───────▼────────────┐    │
  │ UAM.AdaptiveEngine │    │
  │ recommendations    │    │
  │ approval boundary  │    │
  └───────┬────────────┘    │
          │                 │
          └────────┬────────┘
             ┌─────▼─────┐
             │  UAM.Cli  │
             └───────────┘
```

`UAM.Analytics` and `UAM.AdaptiveEngine` must never reference `UAM.Data`, a UI,
an input driver, or a game integration. That boundary is what allows the
mathematics to be tested without a virtual mouse or a running game.

## Data flow

```text
JSON / future provider
        │
        ▼
TrainingObservation  ──►  ObservationValidator
        │                       │
        │ accepted              │ rejected (reported, not modeled)
        ▼                       ▼
PerformanceAnalyzer       AnalysisReport
        │
        ├── DirectionalEstimate[8]
        └── SpeedEstimate[3]
        │
        ▼
PlayerModel (global) ──┐
                       ├──► OptimizationEngine ──► OptimizationRecommendation
PlayerModel (game)  ───┘                              │
                                                      ├── IsActionable?
                                                      ├── RequiresApproval?
                                                      ▼
                                              RecommendationApprover
                                                      │
                                                      ▼
                                                 GameProfile
                                                      │
                                                      ▼
                                                  ProfileJson
```

## Key invariants

1. Domain models in `UAM.Core` do not depend on EF Core, Avalonia, or any OS API.
2. A recommendation is never an approved profile. Applying or exporting requires
   `RecommendationApprover`.
3. A gain can only move when confidence meets the configured minimum.
4. Every proposed gain stays inside `MaximumAdjustment` of neutral.
5. Game profiles contain adjustment parameters, not copies of the player model.
6. Invalid observations never influence a model and are never persisted.
7. Analysis is deterministic for a fixed set of observations and options, except
   for timestamps recorded as metadata.

## Persistence

`TrainingStore` owns a `UamDbContext`. Persistence entities are deliberately
separate from domain records so database concerns cannot leak into the engine.

- `TrainingSessions` is keyed by `(SessionId, GameId)` and stores summary fields.
- `TrainingObservations` stores each accepted observation, indexed by
  `(GameId, TimestampUtc)`.
- The database is created with `EnsureCreated` for v0.1. Schema migrations are a
  prerequisite for public distribution.

## Future extension points

- `PerformanceAnalyzer` can move from eight sectors to a continuous directional
  function without changing transport formats.
- A desktop UI should call the same engine and approval service as the CLI.
- Providers should produce `TrainingObservation` and nothing else; transport
  details belong in adapters outside the engine.
- Confidence should eventually incorporate dispersion and tracking quality, not
  only sample count.
