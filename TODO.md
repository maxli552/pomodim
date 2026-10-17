# TODO — Pomodim (T008)

Protocol: work the top unchecked item, mark it done in the same commit.

## v1

- [x] Scaffold solution: `src/Pomodim.Core`, `src/Pomodim.App`, `tests/Pomodim.Core.Tests`
- [x] `IBrightnessBackend` + `Mock` backend with unit tests
- [x] `WindowsDdc` backend (Dxva2 P/Invoke + WMI fallback) with `--diag`
- [x] Break engine (pure logic, fake clock) + tests for 20/20/20, 20/5, 45/5, custom
- [x] CLI: `--diag`, `--set`, `--display`, `--break now`
- [x] Tray icon + flyout + slider (Avalonia)
- [x] Break dimming with guaranteed restore (exit + signal)
- [x] Optional tray-only timer text
- [x] Autostart toggle (Windows Run key, macOS LaunchAgent, Linux .desktop)
- [ ] `macOS` backend (IOKit + DisplayServices)
- [ ] OS session-lock detection (Windows input desktop) so "never dim while locked" is automatic
- [ ] `Linux` backend (sysfs + ddcutil)
- [ ] Publish all four RIDs

## Later

- [ ] Ambient-light matching
- [ ] "Do not dim while frontmost app is colour-critical" auto-lock
- [ ] Compatibility matrix report (`--diag --json`, copyable)
