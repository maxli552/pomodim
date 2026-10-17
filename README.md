# Pomodim

A tray brightness app with a Pomodoro-shaped break dimmer. During a break the screen dims
instead of a popup appearing.

**Status: early development.** Only the v1 *slice* exists — the core brightness abstraction, a
mock backend, and `dim --diag` on the CLI. There is **no GUI yet**; the tray, flyout, slider and
break dimming are next. Nothing here is usable as a brightness tool at this point.

The design thesis, the confirmed requirements and the acceptance criteria live in [PLAN.md](PLAN.md).
The backlog is [TODO.md](TODO.md); the current task list is [TASKS.md](TASKS.md).

## Design principles

- **Hardware backlight only.** Brightness moves the physical backlight, never gamma, so colours
  stay trustworthy. Gamma is allowed solely for the break dim, only where no backlight control
  exists, and the UI must say so.
- **Breaks are physical, never a popup.** The timer is optional and lives in the tray only.
- **Protocols are yours.** `20/20/20`, `20/5`, `45/5`, or custom.
- **The core is portable logic.** `Pomodim.Core` has no UI and no OS calls; every OS lives behind
  `IBrightnessBackend`.

## Build and test

    dotnet build
    dotnet test
    dotnet run --project src/Pomodim.Cli -- --diag --backend mock

## Why there is a mock backend

A virtual machine has no physical display and no I2C bus, so DDC/CI cannot be tested in a VM.
The mock backend is how the shell (tray, break engine, CLI, config) is tested anywhere, while real
backlight control is verified on hardware: Windows on a ThinkPad T14s, Linux in a VM for the shell
only, macOS on Apple Silicon.

## Licence

Apache-2.0 — see [LICENSE](LICENSE).
