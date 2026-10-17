# Pomodim (T008)

Tray brightness app with a Pomodoro-shaped break dimmer. Cross-platform (.NET 8 + Avalonia).
Read `PLAN.md` first — it is the product spec and the acceptance criteria.

## Rules for this repo

- One branch per feature; never commit to `main` directly during agent runs.
- `TODO.md` is the backlog. Mark items done there, not in chat.
- The break engine and the brightness backends stay free of UI dependencies: pure logic in
  `src/Pomodim.Core`, OS access behind `IBrightnessBackend`, UI in `src/Pomodim.App`.
- Never silently alter colour: hardware backlight first; gamma only for break dimming and only
  when the backend reports no backlight control, and the UI must say so.
- Tests are the gate: `dotnet test` must pass before any review request.
- The person's machine is the only place DDC/CI can be verified. VMs cannot.

## Build

    dotnet build
    dotnet test
    dotnet publish src/Pomodim.App -r win-x64 --self-contained -p:PublishSingleFile=true
