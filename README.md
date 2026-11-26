# Universal Adaptive Mouse (UAM)

**UAM Core v0.1** — a provider-independent engine for learning a player's aiming
bias and generating conservative, game-specific optimization profiles.

This milestone is intentionally a headless foundation. It does **not** intercept
mouse input, install drivers, or integrate with a game. The mathematics can be
developed, tested, and validated without any of those dependencies.

---

## Status

Implemented in v0.1:

- Provider-neutral training observation model with JSON serialization.
- Pre-analysis validation (finite values, plausible timestamps, outlier guards).
- Directional (8-sector) and speed-band performance modeling.
- Confidence-weighted hybrid optimization recommendations.
- Explicit approval boundary between a recommendation and an approved profile.
- Game-specific profiles with a shared global player model as the prior.
- Local SQLite session storage (EF Core).
- Profile export to JSON.
- CLI for the full pipeline: load → analyze → recommend → store → approve → export.
- 71 automated tests covering the engine, analytics, validation, storage, and
  serialization.

Deliberately not included yet:

- Mouse interception or transformation.
- Virtual HID drivers.
- Automatic Aimlabs integration.
- Anti-cheat-sensitive game integrations.
- Desktop UI (scheduled for v0.2).

---

## Architecture decisions

### 1. Hybrid profiles

There is one underlying `PlayerModel` per player. A `GameProfile` stores only
adjustment parameters (`DirectionalGains`, `SpeedGains`, base sensitivity, DPI,
and in-game sensitivity) rather than a copy of the model. Game evidence is
blended with the global model using a pseudo-count prior, so a new game starts
from the player's general tendencies instead of a neutral blank slate.

### 2. Hybrid learning

The engine may analyze data continuously, but calculated changes are represented
as an `OptimizationRecommendation` and cannot be exported or applied until an
explicit approval step. Recommendations below the confidence gate or below the
noise floor are non-actionable and never reach the approval gate.

### 3. The engine is independent of the mouse input mechanism

`UAM.Analytics` and `UAM.AdaptiveEngine` depend only on `UAM.Core` models. They
have no knowledge of operating systems, drivers, games, or input APIs. This is
enforced by project references:

```text
UAM.Core            domain models, validation, serialization
   ↑        ↑
UAM.Analytics   UAM.AdaptiveEngine → UAM.Analytics
   ↑        ↑
UAM.Data    UAM.Cli → UAM.Data
```

A future input layer or desktop UI can consume the same engine without changing
the mathematics.

More detail: [`docs/architecture.md`](docs/architecture.md).

---

## Requirements

- .NET 10 SDK (LTS)
- Linux, Windows, or macOS for the core/CLI
- Visual Studio 2026, Rider, or VS Code for editing

On this machine the SDK is installed side-by-side in `~/.dotnet` so it does not
disturb the system .NET 6 installation. Add it to your shell once:

```bash
export PATH="$HOME/.dotnet:$PATH"
```

To make it permanent, add that line to `~/.zshrc` or `~/.bashrc`. Verify with:

```bash
dotnet --version   # expected: 10.0.x
```

To install .NET 10 elsewhere:

```bash
curl -sSL https://dot.net/v1/dotnet-install.sh -o dotnet-install.sh
chmod +x dotnet-install.sh
./dotnet-install.sh --channel 10.0
```

---

## Build and test

```bash
export PATH="$HOME/.dotnet:$PATH"

dotnet restore
dotnet build
dotnet test
```

---

## Run the pipeline

The two-observation sample is a serialization smoke test. It correctly produces
a **non-actionable** recommendation because confidence is zero below ten
samples per bucket:

```bash
dotnet run --project src/UAM.Cli -- samples/training-session.json
```

The extended synthetic sample has ten observations per direction sector, so it
exercises the full recommendation path:

```bash
dotnet run --project src/UAM.Cli -- \
  analyze samples/training-session-extended.json \
  --save \
  --db uam.db
```

Store and inspect sessions:

```bash
dotnet run --project src/UAM.Cli -- sessions --db uam.db
```

Export requires explicit approval. Without `--approve`, the CLI refuses and
exits with code `2`:

```bash
dotnet run --project src/UAM.Cli -- \
  analyze samples/training-session-extended.json \
  --export out/valorant-profile.json

# After reviewing the recommendation:
dotnet run --project src/UAM.Cli -- \
  analyze samples/training-session-extended.json \
  --export out/valorant-profile.json \
  --approve
```

### CLI reference

```text
UAM.Cli analyze <training-session.json> [options]
UAM.Cli sessions [--db <path>]

Analyze options:
  --game <id>       Game to profile (default: first observation's game)
  --player <id>     Player id (default: player-001)
  --db <path>       SQLite database path (default: uam.db)
  --save            Store accepted observations locally
  --export <path>   Export the approved profile as JSON
  --approve         Explicitly approve the recommendation for export
  --help            Show help
```

The shorthand `UAM.Cli session.json` remains supported.

---

## How the v0.1 model works

1. **Validate.** Non-finite numbers, out-of-range directions, negative speeds,
   implausible timestamps, extreme error ratios, and undefined movement kinds
   are rejected before analysis.
2. **Group.** Observations are grouped into eight 45° direction sectors
   (0 = right, counter-clockwise) and three experimental speed bands
   (< 300 px/s, 300–899 px/s, ≥ 900 px/s).
3. **Estimate.** Each bucket reports sample count, mean signed error ratio, and
   confidence. Signed error is positive for overshoot and negative for
   undershoot. Confidence is `0` below 10 samples, otherwise
   `n / (n + 30)`.
4. **Blend.** Game-specific estimates are blended with the global model using
   `w = n / (n + 30)`. Missing game data leaves the global estimate in charge.
5. **Correct.** `correction = -0.15 × error × confidence`, clamped to ±2%.
   Corrections below 0.1% are treated as noise and left neutral. If confidence
   is below 20%, the gain stays at 1.0.
6. **Gate.** A recommendation is actionable only if at least one gain moved
   beyond the noise floor. Actionable recommendations always require approval.
7. **Export.** The approved `GameProfile` is serialized with camelCase JSON and
   string enum keys.

These constants are engineering defaults, not validated physiological
thresholds. The relationship between endpoint error and optimal sensitivity is
still a research question (see v0.4).

---

## Repository layout

```text
src/
  UAM.Core/            domain models, validation, JSON formats
  UAM.Analytics/       directional/speed performance analysis
  UAM.AdaptiveEngine/  recommendations, approval boundary
  UAM.Data/            EF Core + SQLite session storage
  UAM.Cli/             end-to-end test interface
tests/
  UAM.AdaptiveEngine.Tests/   engine, analytics, validation, data, serialization
samples/
  training-session.json          2-row serialization smoke test
  training-session-extended.json 80-row synthetic pipeline fixture
docs/
  architecture.md
```

---

## Known limitations

- The optimization math is a prototype, not a validated aiming algorithm.
- Confidence currently uses sample count only, not dispersion or tracking quality.
- The global model includes the selected game's observations as well as other
  games; this is acceptable for v0.1 but should become an explicit leave-one-game
  prior design later.
- Speed bands are global defaults, not calibrated per player.
- SQLite schema creation uses `EnsureCreated`; migrations will be added before
  public distribution.
- Re-importing the same file appends duplicate observations. Import idempotency
  and source provenance are planned.
- Evaluation is offline. There is no controlled experiment yet proving that the
  proposed gains improve aim.

---

## Roadmap

| Version | Focus |
| --- | --- |
| v0.1 | Core models, analytics, recommendations, CLI (current) |
| v0.2 | Avalonia desktop app: dashboard, directional heatmap, profiles, history, approval workflow |
| v0.3 | Aimlabs telemetry investigation and connector |
| v0.4 | Controlled validation of directional and velocity models |
| v0.5 | Input sandbox independent of games and anti-cheat |
| v1.0 | Windows release, supported input integration, additional providers |

### Aimlabs research questions

Before designing the final connector, establish which of these are available
through legitimate telemetry or supported integration points:

- starting mouse position and target position
- full movement trajectory
- target acquisition time
- endpoint error and overshoot/undershoot direction
- movement speed

The analytics layer is deliberately provider-neutral so a second data source can
be added without making Aimlabs a hard dependency.
