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
