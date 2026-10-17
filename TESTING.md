# Testing Pomodim on the T14s

Two files matter here, both self-contained (no .NET install needed):

- `dim.exe` — the command-line diagnostic. Run this **first**.
- `Pomodim.exe` — the tray app (tray icon, flyout with the brightness slider, break timer).

## 1. Diagnose first (the important step)

Open PowerShell in the extracted folder:

    .\dim.exe --diag

Expected on a laptop with an external monitor:

    <display name> | backend=windows-ddc | 68/0/100
    <internal panel> | backend=windows-wmi | 45/0/100

What the output tells us:

| Output | Meaning |
|---|---|
| `backend=windows-ddc` with `current/min/max` | external monitor accepts DDC/CI — hardware brightness works |
| `backend=windows-wmi` | internal laptop panel — brightness works through WMI |
| `unsupported: <reason>` | this display cannot be controlled; the reason says why |
| exit code 3 | nothing on this machine can be controlled |

Paste the whole output back — it decides whether the slider can work at all on this
screen, and it is the only place in the project where real hardware is tested.

    .\dim.exe --diag; echo "exit=$LASTEXITCODE"

## 2. The tray app

    .\Pomodim.exe

Expect: a tray icon, and a flyout when you click it, with a brightness slider, a protocol
selector (`20/20/20`, `20/5`, `45/5`, `Custom`), a dim-depth slider, and three switches
(show timer, start with Windows, never dim while locked).

Checks worth doing, in order:

1. **Slider** — drag it; the screen should change brightness while dragging (it debounces
   ~180 ms). Slider position on open should match the real brightness.
2. **Timer in tray** — turn on *Show timer*; hover the tray icon: tooltip shows
   `Pomodim — work 21:59`. With it off, the tooltip is just `Pomodim`, and no timer is
   ever drawn anywhere else.
3. **Break now** — click it. The screen should dim to the dim-depth value and the tray menu
   item *Break now* is the trigger. After the break length (20 s for `20/20/20`) brightness
   must return to **exactly** what it was.
4. **Custom protocol** — choose `Custom`, set work 1 min / break 10 s: after one minute the
   screen should dim for ten seconds on its own.
5. **Lock** — tray menu → *Lock (never dim)*, then *Break now*: nothing should dim while
   locked.
6. **Start with Windows** — switch it on, then open Task Manager → Startup apps; "Pomodim"
   should be listed. Switch it off; it disappears. No elevation prompt at any point.
7. **Quit** — tray menu → *Quit*; the icon disappears and the screen brightness is restored
   if a break was running.

## If something fails

Run the diagnostic and include:

- the `--diag` output (above),
- which step failed,
- whether the monitor's own OSD has a DDC/CI setting and whether it is on.

Do not run the app as administrator to "fix" it: nothing in Pomodim requires elevation, and
if it needs admin to see a display, that is a bug worth knowing about.
