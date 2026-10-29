# Race Imperialis checks

Run `dotnet run --project Tests/RaceImperialis.Tests.csproj` from the repository
root. It uses the production game assembly, this repository's assets, and the
engine submodule. Only keyboard and mouse input are substituted.

`RacingProjectChecks` covers asset ownership, both difficulty profiles, starting
grid/countdown locking, driving, pause/resume, clean restart state, and finishing
after the configured ordered laps.

`car-driving-browser-check.mjs` uses real browser input and the retained UI display
list to check direct startup, rendering, difficulty changes, countdown, driving,
both cameras, pause/resume, mid-race restart, standings, results, and results restart.
The short-race fixture changes invisible gates and the lap limit while keeping
the real map, physics, rendering, and UI.

See [the root README](../README.md#checks) for browser setup and commands. Shared
engine regression checks remain in [the engine submodule](../Engine/README.md).

`column-interlacing-browser-check.mjs` checks startup graphics configuration,
live mode changes, column phases and GPU dispatch counts at an odd viewport width,
compact render-target widths, and full-frame priming after mode changes and race
restarts. GPU readback while
moving the camera verifies that skipped columns retain their exact previous colors.
It also checks the separate HUD and releases the retained texture in full mode.

`column-rendering-performance.mjs` compares CPU frame time, FPS, and optional GPU
timestamps at 1920x1080 in a paused race. GPU timings include history copies and
barriers; CPU timing includes the simulation and UI. It runs the three modes in
both orders, overrides the FPS cap for the test, and saves results under
`artifacts/column-rendering/performance.json`. Browser compositing is outside the
GPU timing. Run browser scripts sequentially, since they change browser focus.
