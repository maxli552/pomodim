# DONE — Pomodim v1 slice, tasks 1–5

Branch: `feat/break-engine` (commits `693d6db` scaffold + `c7c30ee` implementation).
Protected files (PLAN.md, CLAUDE.md, AGENTS.md, TODO.md, TASKS.md) untouched.
No Avalonia, no UI project, no OS/P-Invoke code.

## dotnet test summary (exact line)

```
Passed!  - Failed:     0, Passed:    30, Skipped:     0, Total:    30, Duration: 16 ms - Pomodim.Core.Tests.dll (net8.0)
```

## Files created

**Task 1 — scaffold** (sln pre-existed on the branch; projects wired in it)
- `Pomodim.sln` (already present, kept as-is)
- `src/Pomodim.Core/Pomodim.Core.csproj` (net8.0, no UI/OS references)
- `src/Pomodim.Cli/Pomodim.Cli.csproj` (net8.0, `AssemblyName=dim`, refs Core)
- `tests/Pomodim.Core.Tests/Pomodim.Core.Tests.csproj` (xunit 2.8.1, refs Core)

**Task 2 — `IBrightnessBackend` in Core**
- `src/Pomodim.Core/Brightness/DisplayInfo.cs` — `Id`, `Name`, `BackendName`, `Min`, `Max`, `Current`, `Supported`, `Reason`
- `src/Pomodim.Core/Brightness/IBrightnessBackend.cs` — `GetDisplays()`, `SetBrightness(id, value)`, `Snapshot()`, `Restore(snapshot)`
- `src/Pomodim.Core/Brightness/BrightnessSnapshot.cs`

**Task 3 — Mock backend**
- `src/Pomodim.Core/Brightness/MockBrightnessBackend.cs` — deterministic default set
  (mock-1 0–100 @80, mock-2 10–90 @50, mock-3 unsupported with explicit reason),
  clamps to `[Min, Max]`, records every call (`Calls`), snapshot/restore.

**Task 4 — break engine (pure logic)**
- `src/Pomodim.Core/Break/IClock.cs` — injected clock + `SystemClock`
- `src/Pomodim.Core/Break/SessionState.cs` — `Work`/`Break`/`Paused`/`Locked` + `BreakPhase`
- `src/Pomodim.Core/Break/BreakProtocol.cs` — validated work/break durations
- `src/Pomodim.Core/Break/Protocols.cs` — `20/20/20` (22 min work / 20 s break, per PLAN.md), `20/5`, `45/5`, `Custom`
- `src/Pomodim.Core/Break/BreakEngine.cs` — tick-driven state machine, dim depth, no OS/timer calls

**Task 5 — backend selection + `dim --diag`**
- `src/Pomodim.Cli/Program.cs`
- `src/Pomodim.Cli/DimCli.cs` — `--diag`, `--backend <name>` (else first supported backend);
  one line per display (`name | backend=… | current/min/max` or `unsupported: <reason>`);
  exit 0 with ≥1 controllable display, 3 otherwise, 2 on usage errors
- `src/Pomodim.Cli/BackendRegistry.cs` — default backend list (mock; real backends join later)

**Tests**
- `tests/Pomodim.Core.Tests/FakeClock.cs`
- `tests/Pomodim.Core.Tests/MockBrightnessBackendTests.cs` — clamping, unsupported reason text, call recording, restore-to-snapshot
- `tests/Pomodim.Core.Tests/BreakEngineTests.cs` — preset schedules, work→break→work rollover, pause/resume, locked-never-dims
- `tests/Pomodim.Core.Tests/BreakProtocolTests.cs` — validation

## Acceptance checks run

- `dotnet test` → exit 0 (line above)
- `dotnet run --project src/Pomodim.Cli -- --diag` → 3 lines, exit 0
- `rg -n "Avalonia|DllImport|System.Runtime.InteropServices" src/Pomodim.Core` → no matches
- `dotnet run --project src/Pomodim.Cli -- --diag --backend mock` → one line per mock display, exit 0
- Exit 3 with only unsupported displays → verified via `DimCli.Run(args, backends)` with an
  injected mock (throwaway harness, since the default mock set always includes supported
  displays); also exit 3 with zero displays, exit 2 for unknown backend name

## Could not finish / notes

- **Task 6 (slice gate) not done** — out of scope per instructions ("items 1 through 5").
  The end-to-end CLI test is not in the test project; the CLI exit codes were verified
  manually as above. `git diff --stat main...HEAD` touches only `Pomodim.sln`,
  `src/Pomodim.Core`, `src/Pomodim.Cli`, `tests/` (plus `TASKS.md`/`.gitignore` from the
  pre-existing PM commit, not from this work).
- **Exit-3 path is not reachable from the command line with the default registry**: the
  default mock set always contains supported displays, so `dim --diag` on this machine
  always exits 0. The exit-3 behaviour is implemented and testable via the injectable
  backend list in `DimCli.Run` (the task-6 e2e test can drive it).
- **Parallel scaffold commit**: commit `693d6db` ("scaffold solution … task 1") landed on
  this branch from another session while I was working. I kept its `Pomodim.cs` version
  marker and `ScaffoldTests.cs` (added the global `Xunit` using to the test csproj so it
  compiles) and replaced its placeholder csproj/Program.cs with the final versions.
- **Lock semantics decision**: `Lock()` freezes the phase (like pause) and suppresses
  dimming; `Unlock()` resumes the phase with its remaining time. Documented in
  `BreakEngine` doc comments; a locked session is never in the dimmed state.