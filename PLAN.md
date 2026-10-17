# Pomodim — plan v1

A Pomodoro-shaped brightness dimmer for people who stare at a screen all day.
Name: **Pomodim** (T008). CLI binary: `dim`. Launch thesis: *eye comfort without wellness theatre*.

## The one-line product

A tray app with a brightness slider and a break timer. During a break the screen dims
instead of a popup appearing. Protocols are user-defined, defaulting to the classic ones.

## Confirmed requirements (from the person, 2026-10-17)

1. GUI: tray icon + flyout containing a brightness slider (per display) — no installer,
   autostart optional.
2. Break protocols, user-defined, with presets: `20/20/20` (22 min work / 20 s break / look 20 ft),
   `20/5` (20 min work / 5 min break), `45/5`, and `custom` (any work + break length).
3. During a break the screen dims (to a configurable depth). It must never dim while a
   colour-critical session is locked ("don't dim while I'm proofing").
4. The timer is optional: the user chooses whether it is shown at all. If shown, it lives
   **in the tray only** (tooltip / tray title), never as a window or a toast.
5. Platforms: Windows, Linux, macOS. Same core behaviour on all three.
6. Hardware backlight only where available; never silent gamma dimming. A gamma-based dim
   is allowed only for the break dim on displays without backlight control, and must be
   stated in the UI.

## Architecture (decided)

- **Avalonia (.NET 8)** — one codebase, `TrayIcon` + a custom borderless flyout window.
  Published self-contained per RID: `win-x64`, `win-arm64`, `linux-x64`, `osx-arm64`.
- `IBrightnessBackend` interface with implementations:
  - `WinDdc` — Dxva2.dll (`GetPhysicalMonitorsFromHMONITOR`, `GetVCPFeatureAndVCPFeatureReply`,
    `SetVCPFeature` VCP 0x10) + WMI `WmiSetBrightness` for internal panels.
  - `MacIOKit` — `IOAVServiceWriteI2C` for external DDC, `DisplayServicesSetBrightness` for the
    internal panel.
  - `LinuxSysfs` — `/sys/class/backlight/*/brightness`, else `ddcutil` if installed.
  - `Mock` — in-memory, used by tests and by CI/VM runs where no real display exists.
- Break engine is pure logic (no UI, no OS): testable with a fake clock.
- Config in `%APPDATA%/Pomodim/settings.json` (or the platform equivalent); no registry writes
  except the autostart `Run` value on Windows.

## Where each platform is verified

- **Windows** — real hardware only: the T14s. A VM reports zero physical monitors, so DDC/CI
  cannot be exercised there. No Windows VM will be provisioned for this project.
- **Linux** — a Debian 12 VM on x99 for the shell (tray, break engine, config, CLI) with the mock
  backend; backlight paths need real hardware.
- **macOS** — the Apple Silicon Mac, both the internal panel and an external DDC display.

## Why the mock backend matters

A VM has no physical display and no I2C bus, so DDC/CI **cannot** be verified in a VM.
VMs verify the shell: tray, slider, break engine, dim path via mock/gamma, config, autostart,
CLI. Real backlight verification happens on the T14s (Windows) and the Mac.

## Acceptance criteria for v1 (each must be checkable by a command)

1. `dim --diag` lists every display with: name, backend used, current/min/max, and an explicit
   reason when unsupported. Exit code 0 when at least one control is available, 3 otherwise.
2. `dim --set 40` sets brightness on all controllable displays; `dim --set 40 --display 2` on one.
3. `dim --break now --seconds 20` dims to the configured depth, then restores the exact prior
   values, even if the process is interrupted (restore on exit and on signal).
4. Break engine: given work/break/dim-depth settings it produces the expected schedule;
   unit tests cover rollover, pause, and "locked" (never dim while locked).
5. Tray: icon present, flyout opens with a slider, slider moves brightness; timer text appears
   in the tray only when "show timer" is enabled.
6. Autostart toggle writes/removes the platform autostart entry and is idempotent.
7. `dotnet test` green; the app builds for all four RIDs.

## Out of scope for v1

Gamma long-term dimming, ambient-light automation, per-app profiles, cloud sync, updates.
