# Release notes

## v0.3 — canonical imports and UAM Lab

- Added the canonical `TrainingDocument` envelope (`schemaVersion` 1.0) with
  provider, capture method, source session id, observations, and optional raw
  capture. Bare observation arrays remain accepted as legacy documents.
- Import identity is provider + source session id + SHA-256 content hash.
  Re-importing a document reports `Duplicate`, updates import metadata only, and
  writes no observations.
- Added `ImportRecords` and an additive schema upgrade so existing `uam.db`
  files open without data loss.
- Added `UAM.Cli import <file>` and `UAM.Cli import --folder <path>`. Folder
  imports are idempotent and never move or delete source files.
- Added a provider registry with maturity and terms positions. Imports are
  refused for providers whose terms are not recorded as `Permitted`.
- Added UAM Lab, a first-party 2D pointer task. Completed trials are imported
  automatically and appear in Dashboard and History with no manual JSON.
- Added `samples/training-document.json` for manual import verification.

Recorded limitation: UAM Lab measures 2D pointer flicks on a flat canvas, not
first-person camera aiming. It cannot model 3D sensitivity, FOV, or game-engine
acceleration.

## v0.2 — Avalonia desktop app

- Added the Avalonia desktop shell with seven pages: Dashboard, Analysis,
  Optimization, Profiles, Integrations, History, and Settings.
- All page numbers come from application services backed by SQLite. Empty
  databases show empty states, never placeholder metrics.
- Recommendations remain separate from approved profiles. Approval is a
  two-step action routed through `RecommendationApprover`.
- Added the Integrations page with the legacy provider list, the provider
  registry, and the UAM Lab task.
