# REVIEW — Pomodim v1 slice (T008)

Reviewer pass over `feat/break-engine` vs `main`, against `PLAN.md` + `TASKS.md` (slice: tasks 1–6).

- Branch: `feat/break-engine` (`b12eed6` → `47ababc` → `693d6db` → `c7c30ee`), clean tree.
- Diff: 24 files, +1097/−0. Nothing in the diff is deleted or rewritten.
- Environment: macOS arm64, .NET 8, `dotnet build` 0 warnings / 0 errors.

## Evidence actually run

| Command | Result |
|---|---|
| `dotnet build` | Build succeeded, 0 Warning(s), 0 Error(s) |
| `dotnet test` | `Passed! - Failed: 0, Passed: 30, Skipped: 0, Total: 30` |
| `dotnet run --project src/Pomodim.Cli -- --diag` | 3 lines printed, exit **0** |
| `dotnet run --project src/Pomodim.Cli -- --diag --backend mock` | 3 lines printed, exit **0** |
| `dotnet run --project src/Pomodim.Cli -- --diag --backend nope` | `unknown backend 'nope' (available: mock)`, exit 2 |
| `rg -n "Avalonia\|DllImport\|System.Runtime.InteropServices" src/Pomodim.Core` | no matches (exit 1) — also no matches with `--no-ignore` outside `bin/`/`obj/` |
| `git diff --stat main...HEAD` | touches `.gitignore`, `TASKS.md` **in addition to** the allowed paths |

## PASS/FAIL table — one row per TASKS.md acceptance criterion

| # | Task 1 — Scaffold | Verdict | Evidence / citation |
|---|---|---|---|
| 1a | `dotnet test` exits 0 | **PASS** | 30/30 passing, exit 0 |
| 1b | `dotnet run --project src/Pomodim.Cli -- --diag` runs | **PASS** | prints 3 display lines, exit 0 |
| 1c | Three projects wired in `Pomodim.sln`, Core+Cli on `net8.0`, Core free of Avalonia/OS API | **PASS** | `Pomodim.sln:8,10,14`; `src/Pomodim.Core/Pomodim.Core.csproj:4`; `src/Pomodim.Cli/Pomodim.Cli.csproj:5` |

| # | Task 2 — `IBrightnessBackend` in Core | Verdict | Evidence / citation |
|---|---|---|---|
| 2a | Descriptor has `Id,Name,BackendName,Min,Max,Current,Supported,Reason` | **PASS** | `src/Pomodim.Core/Brightness/DisplayInfo.cs:8-16` |
| 2b | `GetDisplays`/`SetBrightness`/`Snapshot`/`Restore` present | **PASS** | `src/Pomodim.Core/Brightness/IBrightnessBackend.cs:13-26` |
| 2c | `rg -n "Avalonia\|DllImport\|System.Runtime.InteropServices" src/Pomodim.Core` → none | **PASS** | exit 1, zero matches |

| # | Task 3 — Mock backend | Verdict | Evidence / citation |
|---|---|---|---|
| 3a | Deterministic displays, backend named `mock` | **PASS** | `MockBrightnessBackend.cs:61,67-79`; `MockBrightnessBackendTests.cs:9-14` |
| 3b | At least one unsupported display with explicit `Reason` | **PASS** | `MockBrightnessBackend.cs:71-78`; `MockBrightnessBackendTests.cs:17-23` |
| 3c | Setter clamps to `[Min,Max]` | **PASS** | `MockBrightnessBackend.cs:92`; `MockBrightnessBackendTests.cs:25-47` |
| 3d | Setter records calls | **PASS** | `MockBrightnessBackend.cs:94`; `MockBrightnessBackendTests.cs:50-60` |
| 3e | `Restore` returns every display to its snapshotted value | **PASS** | `MockBrightnessBackend.cs:100-114`; `MockBrightnessBackendTests.cs:91-106` |

| # | Task 4 — Break engine | Verdict | Evidence / citation |
|---|---|---|---|
| 4a | Injected clock | **PASS** | `src/Pomodim.Core/Break/IClock.cs:7-16`; `FakeClock.cs:6-21` |
| 4b | Presets `20/20/20`, `20/5`, `45/5`, `custom` | **PASS** | `Protocols.cs:9-20`; `BreakEngineTests.cs:11-26` |
| 4c | States work/break/paused/locked + dim depth | **PASS** | `SessionState.cs:4-16`; `BreakEngine.cs:19-47` |
| 4d | No OS/timer calls inside engine | **PASS** | `BreakEngine.cs` touches only `IClock`; plan-level `rg` of Core is clean |
| 4e | `dotnet test` covers preset schedules, work→break→work rollover | **PASS** | `BreakEngineTests.cs:41-91` |
| 4f | Covers pause/resume | **PASS** | `BreakEngineTests.cs:94-149` |
| 4g | Locked session never enters dimmed state | **PASS** | `BreakEngine.cs:47,112-119`; `BreakEngineTests.cs:152-201` |

| # | Task 5 — Backend selection + `dim --diag` | Verdict | Evidence / citation |
|---|---|---|---|
| 5a | `--backend <name>` selection, else first supported | **PASS** | `DimCli.cs:73-99` |
| 5b | One line per display (name, backend, current/min/max, `unsupported: <reason>`) | **PASS** | `DimCli.cs:110-117`; live output verified |
| 5c | `--diag --backend mock` prints one line per mock display, exits 0 | **PASS** | 3 lines, exit 0 |
| 5d | Exits 3 with only unsupported displays | **FAIL** | Not reachable from the shipped CLI: `src/Pomodim.Cli/BackendRegistry.cs:12-15` registers only the default mock set, and that set always contains two supported displays (`MockBrightnessBackend.cs:69-70`), so no argument combination produces the exit-3 path. No test covers it either. The branch logic at `DimCli.cs:119` looks correct but the criterion is unverified — the submitter confirms this in `DONE.md` ("Exit-3 path is not reachable from the command line with the default registry"). |

| # | Task 6 — Slice gate | Verdict | Evidence / citation |
|---|---|---|---|
| 6a | End-to-end test driving the CLI with the mock backend | **FAIL** | No such test exists. The only test project references just Core — `tests/Pomodim.Core.Tests/Pomodim.Core.Tests.csproj:22` — and `rg -n "Pomodim.Cli\|DimCli" tests/` returns nothing. `DimCli.Run(args, backends)` is public and injectable (`DimCli.cs:25`), so it was testable; `DONE.md` states the exit codes were "verified manually … throwaway harness". |
| 6b | Check that no display means no dim | **FAIL** | Not implemented anywhere. The zero-display path `DimCli.cs:104-108` has no test, and there is no "no displays → do not dim" assertion in Core tests. |
| 6c | `dotnet test` exits 0 including the `--diag` exit-code test | **FAIL** | `dotnet test` is green (30/30) but there is no `--diag` exit-code test, so the clause is unmet by construction. Task 6 as a whole was not attempted (`DONE.md`: "Task 6 (slice gate) not done"). |
| 6d | `git diff --stat main...HEAD` touches only `src/Pomodim.Core`, `src/Pomodim.Cli`, `tests/`, `Pomodim.sln` | **FAIL** | Diff also contains `TASKS.md` (43 lines, added on this branch by `47ababc`) and `.gitignore` (+1, `!TASKS.md`). These were authored by the PM commit but landed on `feat/break-engine` and never on `main`, so they are inside `main...HEAD` — see `.gitignore:7` and `TASKS.md:1`. |

## Non-blocking findings (fix alongside the FAILs, not acceptance criteria)

1. **Restore pollutes the call log.** `MockBrightnessBackend.cs:112` routes `Restore` through `SetBrightness`, which appends to `Calls` (`MockBrightnessBackend.cs:94`). After a snapshot/restore cycle, `Calls` no longer distinguishes user writes from undo writes — relevant once `--set`/`--break` assert on recorded calls. Prefer writing `display.Current` directly in `Restore`.
2. **Schedule drift after a late tick.** `BreakEngine.cs:78,84` sets `_deadline = now + Duration` instead of `_deadline + Duration`, so every phase silently shifts by the tick lateness. Documented at `BreakEngine.cs:62-65`, but a 20/5 protocol ticked once a minute will wander; anchor on the previous deadline and only then catch up.
3. **Unlock forgets a prior pause.** `BreakEngine.cs:112-119` allows `Lock()` from `Paused` (keeping `_heldRemaining`) but `Unlock()` at `BreakEngine.cs:122-128` resumes into `Work`/`Break`, silently un-pausing the session. Either reject `Lock()` while paused or restore `Paused`. No test covers it.
4. **Unsupported line drops current/min/max.** `DimCli.cs:116` prints `name | backend | unsupported: reason` with no `current/min/max`, while `PLAN.md:31` asks for "name, backend used, current/min/max, and an explicit reason when unsupported". Keep the range on unsupported lines too.
5. **Stale placeholder comment.** `src/Pomodim.Core/Pomodim.cs:3-6` still says "the break engine and IBrightnessBackend land here in later tasks" — both have landed.
6. **Redundant `using Xunit;`** in `MockBrightnessBackendTests.cs:2`, `BreakEngineTests.cs:2`, `BreakProtocolTests.cs:2` now that the csproj has a global `<Using Include="Xunit" />` (`Pomodim.Core.Tests.csproj:18`).

## Rule violations (CLAUDE.md / AGENTS.md)

| Rule | Status | Citation |
|---|---|---|
| "One branch per feature; never commit to `main` directly during agent runs." | **OK** | All work sits on `feat/break-engine`; `main` is untouched at `b12eed6`. |
| "The break engine and the brightness backends stay free of UI dependencies… UI in `src/Pomodim.App`." | **OK** | Core has no Avalonia/UI reference; no `Pomodim.App` project was created, so the App/UI boundary is intact. |
| "Never silently alter colour: hardware backlight first; gamma only for break dimming…" | **OK (not exercised)** | No gamma path exists in this slice; nothing silently alters colour. |
| "Tests are the gate: `dotnet test` must pass before any review request." | **OK** | 30/30 green before the request. |
| "The person's machine is the only place DDC/CI can be verified. VMs cannot." | **OK** | No DDC/CI claim is made; the mock is used exactly as `PLAN.md:37-41` prescribes. |
| "`TODO.md` is the backlog. Mark items done there, not in chat." | **VIOLATED** | `TODO.md` is byte-identical to `main` (all 16 v1 boxes still `- [ ]`, `TODO.md:6-9` cover this slice's scaffold, backend, engine and CLI work). Completion was reported in `DONE.md` instead, and `TODO.md:3` itself says "mark it done in the same commit". The task-1/2/3 slice items should be ticked in the same commits. |
| `~/dev/CLAUDE.md`: "One Claude session per app folder… Never point two sessions at the same folder." | **VIOLATED (process)** | `DONE.md`: "Parallel scaffold commit … landed on this branch from another session while I was working." Two sessions shared this folder, and the collateral is visible in the diff: the PM commit `47ababc` (`TASKS.md`, `.gitignore`) sits on the feature branch instead of `main`, which is precisely what breaks criterion 6d. |

Protected files `PLAN.md`, `CLAUDE.md`, `AGENTS.md`, `TODO.md` are unmodified — confirmed by an empty `git diff main...HEAD -- PLAN.md CLAUDE.md AGENTS.md TODO.md`.

## Summary

Tasks 1–4 are solid and genuinely tested; task 5's implementation is right but half its acceptance is unverifiable and untested. Task 6 (the slice gate) is entirely absent, and the diff-scope criterion fails because `TASKS.md`/`.gitignore` were committed to this branch rather than `main`. Fix: add a test project reference to `Pomodim.Cli`, ship the `--diag` exit-code tests (0 / 3 / no-displays) plus the no-display-means-no-dim assertion, make the exit-3 case reachable (e.g. a `--backend mock:unsupported-only` fixture or an injected backend list in the test), tick `TODO.md`, and rebase `47ababc` onto `main` so the diff scope matches.

VERDICT: REQUEST CHANGES
