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
---

# DONE — Round 5 (Windows backend cannot control laptop brightness), tasks 1–5

Branch: `fix/windows-backend-round5`. Tasks 1–5 of `TASKS-ROUND5.md` are implemented;
task 6 (`dim --diag --verbose`) was deliberately deferred (see below). Tray app and
`MockBrightnessBackend` untouched.

## dotnet test summary (exact lines)

```
Passed!  - Failed:     0, Passed:    51, Skipped:     0, Total:    51, Duration: 21 ms - Pomodim.Core.Tests.dll (net8.0)
Passed!  - Failed:     0, Passed:    42, Skipped:     0, Total:    42, Duration: 31 ms - Pomodim.Backends.Windows.Tests.dll (net8.0)
Passed!  - Failed:     0, Passed:    10, Skipped:     0, Total:    10, Duration: 16 ms - Pomodim.Cli.Tests.dll (net8.0)
```

## Task 1 — pure VCP interpretation (defect f)

- `src/Pomodim.Core/Brightness/VcpBrightness.cs`: `Interpret(bool succeeded, int vcpCodeType,
  uint current, uint max, int? win32Error)` → `VcpReading(Supported, Min, Max, Current, Reason)`.
  Total, OS-free (no `IntPtr`, no `DllImport`), never throws. `max == 0` is unsupported with a
  reason, `current > max` clamps, `uint.MaxValue` saturates at `int.MaxValue`, momentary code type
  is only fatal when the range is unusable (some monitors report 0 for a working control).
- `DescribeWin32Error` maps the DDC-relevant Win32 codes (5, 6, 31, 87, 1167, 1168) to readable
  text; unknown codes still print the number.
- `tests/Pomodim.Core.Tests/VcpBrightnessTests.cs`: 17 cases (success unswapped, non-100 max,
  failure reasons, max == 0, current > max, garbage input, overflow).

## Task 2 — Dxva2 signatures + BOOL semantics (defects a, b, c, d)

- New `src/Pomodim.Backends.Windows/Dxva2Native.cs`: every P/Invoke now lives here behind
  `IDxva2Native` (`EnumMonitors`, `GetPhysicalMonitors`, `GetVcpFeature`, `SetVcpFeature`,
  `DestroyPhysicalMonitors`).
  - `PHYSICAL_MONITOR` is the SDK struct (handle + 128-char description), not `MONITORINFOEX`.
  - `GetVCPFeatureAndVCPFeatureReply(HANDLE, BYTE, out int pvct, out uint current, out uint max)`,
    current before max.
  - every `BOOL` is read as nonzero = success; `GetLastWin32Error()` on failure.
  - the sizing `GetPhysicalMonitorsFromHMONITOR` call ignores its BOOL (documented
    `ERROR_INSUFFICIENT_BUFFER` behaviour) and uses the count; `count == 0` returns before any
    `[0]` indexing.
  - `szDevice` comes from `GetMonitorInfoW(MONITORINFOEX)`.
- `WindowsMonitorApi` retries a failed capability read exactly once.
- Acceptance: `rg -n "status (==|!=) 0" src/Pomodim.Backends.Windows` → no matches.

## Task 3 — handle lifetime + stable ids

- `WindowsMonitorApi` holds every acquired physical-monitor handle until `Dispose` (backend
  implements `IDisposable` and forwards to the seam); it re-syncs on the HMONITOR set only, so a
  monitor that is still present never has its handle destroyed, and an unplugged one is released.
- Ids come from `szDevice` (`\\.\DISPLAY1` → `ddc-display1`), enumeration index only as fallback —
  no handle in the id. Two `GetDisplays()` calls return identical ids and handles.
- Acceptance: `rg -n "ToInt64" src/Pomodim.Backends.Windows` → no matches.

## Task 4 — WMI fallback (defect e)

- `WmiBrightnessFallback` is now behind `IWmiBrightness`, with `IWmiQuery`/`IWmiObject` seams over
  the `ManagementObject` (so tests see the exact invocation arguments).
- `WmiSetBrightness` is called with `(0u, (byte)level)` — two arguments, timeout 0, byte level —
  its status is checked (0 = success, anything else throws with the status), and the level is
  clamped into byte range.
- `CurrentBrightness` is unboxed with `Convert.ToInt32` (WMI returns a `byte`); `Min`/`Max` come
  from the panel's `Levels` array, falling back to [0, 100]; the current value is clamped into it.
- Panels are keyed by the WMI instance key; `SetBrightness` matches that key, never an index.

## Task 5 — backend mapping and no-throw enumeration (+ the panel-matching fix)

- WMI panels are matched only to DDC-unsupported monitors, in ordinal order among those; an
  external DDC display listed first no longer consumes the laptop panel's WMI entry.
- When both paths fail the DDC `Reason` is kept and the WMI outcome is appended; a throwing WMI
  query becomes a reason, not an exception.
- `Refresh` wraps enumeration: a throwing API yields an unsupported `windows` display named
  `display enumeration` with the exception text, so `GetDisplays()` always returns (CLI exit 3,
  tray app never crashes on an unplug).
- Fakes: `FakeDxva2Native` (native layer), `FakeMonitorApi`/`FakeWmiFallback` (seam), `FakeWmi`
  (WMI objects). New tests: DDC-first + WMI-only panel maps to `backend=windows-wmi` with its
  current value, throwing API → unsupported displays with reasons, WMI failure keeps the DDC
  reason, handles are never destroyed while exposed, ids are stable.

## Not done this round

- **Task 6 (`--diag --verbose`)**: deferred. Per-display enumeration index, DDC name/status and WMI
  levels are not on `DisplayInfo`; adding them means widening the Core record or a second
  diagnostic interface. Left for a dedicated slice.
- **Task 7 (`--set`)**: unchanged, still absent from `DimCli`.
- **Task 8 (T14s hardware gate)**: cannot run off the laptop; the new mapping is what that gate
  must verify. `WindowsBrightnessBackend` now implements `IDisposable`; the tray app does not yet
  dispose it on quit (OS reclaims the handles at process exit).
