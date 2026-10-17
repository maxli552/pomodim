# TODO — Pomodim (T008)

Protocol: work the top unchecked item, mark it done in the same commit.

## v1

- [ ] Scaffold solution: `src/Pomodim.Core`, `src/Pomodim.App`, `tests/Pomodim.Core.Tests`
- [ ] `IBrightnessBackend` + `Mock` backend with unit tests
- [ ] `WindowsDdc` backend (Dxva2 P/Invoke + WMI fallback) with `--diag`
- [ ] Break engine (pure logic, fake clock) + tests for 20/20/20, 20/5, 45/5, custom
- [ ] CLI: `--diag`, `--set`, `--display`, `--break now`
- [ ] Tray icon + flyout + slider (Avalonia)
- [ ] Break dimming with guaranteed restore (exit + signal)
- [ ] Optional tray-only timer text
- [ ] Autostart toggle (Windows Run key, macOS LaunchAgent, Linux .desktop)
- [ ] `macOS` backend (IOKit + DisplayServices)
- [ ] `Linux` backend (sysfs + ddcutil)
- [ ] Publish all four RIDs

## Later

- [ ] Ambient-light matching
- [ ] "Do not dim while frontmost app is colour-critical" auto-lock
- [ ] Compatibility matrix report (`--diag --json`, copyable)
