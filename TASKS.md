# TASKS — Pomodim v1 slice

Slice: break engine + `IBrightnessBackend` abstraction + `Mock` backend + `dim --diag`.
Out of scope here: tray/flyout, `--set`, `--break`, real OS backends, autostart, publishing.

Work top to bottom on one branch. Each task ships with its tests; the reviewer gate is
`dotnet test` plus the stated command.

## 1. Scaffold the solution

`src/Pomodim.Core`, `src/Pomodim.Cli`, `tests/Pomodim.Core.Tests`, wired in `Pomodim.sln`;
Core and Cli target `net8.0`, Core references no Avalonia or OS API.
**Accept:** `dotnet test` exits 0 and `dotnet run --project src/Pomodim.Cli -- --diag` runs (any output, any exit code).

## 2. Define `IBrightnessBackend` in Core

Display descriptor (`Id`, `Name`, `BackendName`, `Min`, `Max`, `Current`, `Supported`, `Reason`),
`GetDisplays()`, `SetBrightness(id, value)`, `Snapshot()`/`Restore(snapshot)`; no UI dependencies.
**Accept:** `rg -n "Avalonia|DllImport|System.Runtime.InteropServices" src/Pomodim.Core` returns no matches.

## 3. Implement the `Mock` backend

In-memory, deterministic displays selected by name (`mock`), including at least one unsupported
display reporting an explicit `Reason`; setters clamp to `[Min, Max]` and record calls.
**Accept:** Core tests assert clamp behaviour, unsupported-display reason text, and that `Restore` returns every display to its snapshotted value.

## 4. Break engine (pure logic)

Injected clock, protocol presets `20/20/20`, `20/5`, `45/5`, `custom`, states work/break/paused/locked,
and a dim depth; no OS or timer-thread calls inside the engine.
**Accept:** `dotnet test` covers preset schedules, work→break→work rollover, pause/resume, and that a locked session never enters the dimmed state.

## 5. Backend selection + `dim --diag`

Select backend by name (`--backend`, else first supported), list every display as one line —
name, backend, current/min/max, `unsupported: <reason>` — and exit 0 when at least one display is
controllable, 3 otherwise.
**Accept:** `dotnet run --project src/Pomodim.Cli -- --diag --backend mock` prints one line per mock display and exits 0, and exits 3 with only unsupported displays.

## 6. Slice gate

End-to-end test driving the CLI with the mock backend, plus a check that no display means no dim.
**Accept:** `dotnet test` exits 0 including the `--diag` exit-code test, and `git diff --stat main...HEAD` touches only `src/Pomodim.Core`, `src/Pomodim.Cli`, `tests/`, and `Pomodim.sln`.
