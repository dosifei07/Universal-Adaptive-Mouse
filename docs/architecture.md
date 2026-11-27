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
- `ImportRecords` stores one row per imported document identity
  (`Provider`, `SourceSessionId`, `ContentHash`), with `LastImportedAtUtc` and
  `ImportCount`. Re-imports touch this row and never re-write observations.
- The schema is created with `EnsureCreated`, then `InitializeAsync` applies
  additive `CREATE TABLE IF NOT EXISTS` statements so an existing v0.2 `uam.db`
  opens and gains `ImportRecords`. Formal migrations are still a prerequisite for
  public distribution.

## Import pipeline and provider policy (v0.3)

```text
Provider / UAM Lab / watched folder
              │
              ▼
   TrainingDocument (schemaVersion, provider,
     captureMethod, sourceSessionId, observations)
              │
              ▼
      TrainingDocumentJson
      validate major version
      compute SHA-256 hash
              │
              ▼
        ImportService
      ProviderRegistry gate ──► ProviderNotPermittedException
              │
              ├── duplicate (provider + session or hash)
              │        └── touch LastImportedAtUtc / ImportCount
              ▼
      TrainingStore observations + ImportRecord
              │
              ▼
   Dashboard / History / ProviderHealth
```

- A canonical document is a JSON object; a bare observation array is still
  accepted and wrapped as `provider = canonical-json`,
  `captureMethod = legacy-json`.
- The schema major version must be `1`; newer majors are rejected with
  `TrainingDocumentVersionException`.
- Import identity is provider + source session id + SHA-256 of the exact content.
  Duplicate detection also matches the hash alone, so white-space-only edits to a
  file still resolve to one session.
- The provider registry records maturity (`Available`, `Research`, `Planned`) and
  a terms position (`Permitted`, `PermissionRequested`, `NotPermitted`).
  `ImportService` refuses any provider whose terms are not `Permitted` before
  reading or storing observations.
- Provider health is derived from real state: `Connected` only when import
  records exist, `Ready` when working code exists without data, `NotConnected`
  for research/planned providers, and `Blocked` for `NotPermitted`.

## UAM Lab (v0.3)

- `LabSessionEngine` lives in `UAM.Application` and has no Avalonia or data
  dependency. The desktop canvas only forwards pointer points and hit flags.
- Each trial becomes one `TrainingObservation` labelled `gameId = uam-lab`,
  `captureMethod = 2d-canvas`.
- `LabSessionService.CompleteAsync` converts the capture, builds the canonical
  document, and runs it through the same `ImportService` as any other provider.
  The user never handles JSON.
- Recorded limitation: this is a 2D pointer task, not first-person camera aiming.
  It cannot measure game sensitivity, FOV, or engine acceleration.

## Future extension points

- `PerformanceAnalyzer` can move from eight sectors to a continuous directional
  function without changing transport formats.
- A desktop UI should call the same engine and approval service as the CLI.
- Providers should produce `TrainingObservation` and nothing else; transport
  details belong in adapters outside the engine.
- Confidence should eventually incorporate dispersion and tracking quality, not
  only sample count.

## Desktop presentation layer (v0.2 batch A)

```text
UAM.Desktop (Avalonia, composition root)
   ├── UAM.Desktop.ViewModels   no Avalonia, no UAM.Data, no UAM.AdaptiveEngine
   │      └── UAM.Application   service interfaces
   └── UAM.Desktop.Shared       MVVM primitives, formatting, navigation catalog
```

- `MainViewModel` owns the seven pages and routing. Pages request navigation
  through `NavigationRequested`; they never reference each other. Navigation is
  refused while the current page has unsaved edits.
- Page loads go through `PageViewModelBase.LoadLatestAsync`: only the newest
  request may update a page, and a superseded request's result or failure is
  discarded.
- Every displayed number comes from an application service. Before data exists,
  pages show an empty state rather than placeholder metrics.
- Approval boundary: loading, generating, selecting and filtering never
  approve. Approval is two steps (begin, confirm) for one actionable pending
  recommendation, and the only route is `IOptimizationService.ApproveAsync`,
  which runs `RecommendationApprover`. `ArchitectureTests` fail the build if the
  view models gain a reference to the data layer or the approval engine.
