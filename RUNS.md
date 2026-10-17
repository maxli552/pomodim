# RUNS — model performance during the Pomodim build

Wall-clock measurements of the three-role loop on this project. Measured by the operator
(`time` around each `pi -p` invocation), not self-reported by the agents. Same machine, same
network, no attempt to isolate GPU load: the x99 box also serves other workloads, so treat
these as *observed throughput for this task*, not a benchmark.

Machine: x99 (Proxmox host, 72 cores, 125 GB RAM) serving `x99-local/qwen3.8-27b-local`.
Reviewer/PM: `deepseek/deepseek-flash` over the network from the Mac.

## Log

| # | Date | Role | Model | Task | Wall | Outcome |
|---|---|---|---|---|---|---|
| 1 | 2026-10-17 | smoke | qwen3.8-27b-local | "reply OK" | **7.5 s** | model reachable, warm |
| 2 | 2026-10-17 | PM | deepseek-flash | write `TASKS.md` (6 tasks) | **21.7 s** | usable on first pass |
| 3 | 2026-10-17 | executor | qwen3.8-27b-local | task 1: solution scaffold + 1 test | **1 m 56 s** | committed, 1/1 tests |
| 4 | 2026-10-17 | executor | qwen3.8-27b-local | tasks 2–5: backend + mock + break engine + CLI | not captured* | committed, 30/30 tests |
| 5 | 2026-10-17 | reviewer | deepseek-flash | review diff vs acceptance | **1 m 3 s** | REQUEST CHANGES, 4 real defects |
| 6 | 2026-10-17 | executor | qwen3.8-27b-local | round 2: 4 defect fixes + new CLI test project | **10 m 9 s** | committed, 30 + 9 tests |
| 7 | 2026-10-17 | executor | qwen3.8-27b-local | round 3: Windows DDC/CI + WMI backend | **>30 m (capped, 37 turns)** | build left broken (CA1416); operator fixed annotations |
| 8 | 2026-10-17 | reviewer | deepseek-flash | round 4: review the Avalonia tray app | **3 m 1 s** | REQUEST CHANGES, 3 blockers |
| 9 | 2026-10-17 | operator | — | Avalonia tray app, flyout, dim/restore, exit hooks, publish | ~45 m | 49 tests green, win-x64 packaged |

\* Run 4 was launched backgrounded and the harness reaped the process handle before it was timed;
the work completed and was verified by the operator. Treat its duration as "between 2 and 20 min".

## What the numbers say

- **A 27B local model is viable for this kind of slice work**, at roughly 2 min for a scaffold and
  10 min for a multi-file fix round. Not interactive, but unattended-viable.
- **Cost per defect round is the interesting number.** Round 2 (4 defects, 1 new test project,
  ~5 files) cost 10 minutes of local inference. The same round on a hosted model would be a
  minute and money; the local model is a minute of nobody's attention and nothing else.
- **Instruction-following held.** In every run the executor kept `Pomodim.Core` free of UI/OS
  code and did not touch the protected files; it also reported scope items it was told not to
  perform rather than performing them, which is what made the review possible.
- **The reviewer is the cheap half of the loop.** 1 minute of a hosted model caught four
  acceptance-criteria failures that a 10-minute local run had reported as done.

## Round 3 is the cautionary one

The same model that finished a clean scaffold and a clean fix round **stalled on the
platform-target plumbing**: it multi-targeted the Windows project as `net8.0;net8.0-windows`,
so the WMI calls tripped the platform-compatibility analyzer on the `net8.0` pass, and 37 turns
in it was still iterating. Capping at 30 minutes and fixing annotations by hand took two minutes.
Lesson for the loop: give the executor *bounded, single-concept* tasks (a backend behind a seam,
a test project) and keep build/TFM plumbing with the operator.

The GUI round was not attempted with the executor at all: Avalonia API surface plus a 30-minute
loop was a worse trade than writing ~600 lines of code-behind directly. The reviewer then found
three blockers in it in 3 minutes — the loop's value is in the review, not only in the writing.

## How to add a row

    /usr/bin/time -p pi --mode json -p --model "x99-local/qwen3.8-27b-local" "<task>" > /tmp/run.json
    # then read the turn_end events in /tmp/run.json for token usage

`--mode json` emits one JSON object per event; `turn_end` carries the assistant message and the
token usage, so a run can be recorded with tokens rather than only wall time.
