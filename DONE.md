# DONE — Pomodim v1 slice, tasks 1–6 (review round 2)

Branch: `feat/break-engine`. This round fixes the reviewer FAILs from `REVIEW.md`:
5d (exit-3 unreachable from the CLI), 6a (no e2e CLI test), 6b (no zero-display check),
6c (no `--diag` exit-code tests). Tasks 1–5 stand as before.
Protected files (PLAN.md, CLAUDE.md, AGENTS.md, TODO.md, TASKS.md, REVIEW.md) untouched.
No Avalonia, no UI project, no OS/P-Invoke code; `src/Pomodim.Core` stays pure logic.

## dotnet test summary (exact lines)

```
Passed!  - Failed:     0, Passed:    30, Skipped:     0, Total:    30, Duration: 19 ms - Pomodim.Core.Tests.dll (net8.0)
Passed!  - Failed:     0, Passed:     9, Skipped:     0, Total:     9, Duration: 1 ms - Pomodim.Cli.Tests.dll (net8.0)
```

## What changed this round

**5d — exit-3 reachable from the CLI**
- `src/Pomodim.Core/Brightness/MockBrightnessBackend.cs` — optional `name` constructor
  parameter (default `"mock"`) and `UnsupportedOnlyDisplays()`: a display set with no
  controllable displays. Pure data, no UI/OS.
- `src/Pomodim.Cli/BackendRegistry.cs` — default registry now ships a second backend,
  `mock-unsupported` (the mock with only unsupported displays).
  `dim --diag --backend mock-unsupported` → exit 3. Default selection still picks `mock`
  (first backend with a supported display), so plain `--diag` still exits 0.

**6a/6c — e2e CLI test project with `--diag` exit-code tests**
- `tests/Pomodim.Cli.Tests/` (new, wired into `Pomodim.sln`) — `DimCliTests` drives
  `DimCli.Run` end to end with the mock backends, asserting exact output lines and exit
  codes: 0 (default registry, `--backend mock`), 3 (`--backend mock-unsupported`),
  2 (no action, unknown argument, `--backend` without name, unknown backend), 0 (`--help`).
  Single test class, so the Console redirection is sequential and safe.

**6b — zero displays means nothing is dimmed**
- `Diag_ZeroDisplays_Exits3_AndDimsNothing`: injected zero-display mock → exit 3, no
  display lines on stdout, `no displays found` on stderr, and `backend.Calls` empty
  (no brightness write was issued).

## Acceptance checks run

- `dotnet build` → 0 warnings / 0 errors
- `dotnet test` → exit 0 (lines above; 39 tests total)
- `dotnet run --project src/Pomodim.Cli -- --diag` → 3 lines, exit 0
- `dotnet run --project src/Pomodim.Cli -- --diag --backend mock-unsupported` → 1 line, exit 3
- `dotnet run --project src/Pomodim.Cli -- --diag --backend nope` → `unknown backend 'nope' (available: mock, mock-unsupported)`, exit 2
- `rg -n "Avalonia|DllImport|System.Runtime.InteropServices" src/Pomodim.Core` → no matches

## Remaining for the PM / operator (not doable from this branch)

- **6d diff scope**: `git diff --stat main...HEAD` still touches `TASKS.md`, `.gitignore`,
  `DONE.md`, `LICENSE`, `README.md`, `REVIEW.md` and `PLAN.md` (+8 lines, added by commit
  `9ac24b8` — note the reviewer's "PLAN.md unmodified" check did not catch this). Those
  governance/doc commits need to land on `main` (e.g. cherry-pick/rebase `47ababc` and
  `9ac24b8`'s doc hunks onto `main`); agent runs may not commit to `main`.
- **TODO.md tick**: the reviewer flagged the slice items as unticked; `TODO.md` is
  protected for this run, so it stays for the operator.