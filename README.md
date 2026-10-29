# Race Imperialis

A Blazor WebAssembly racing game built with [WolfSlop](https://github.com/Lminta/WolfSlop).
Opening the game loads Pine Loop directly. Click the canvas to capture the mouse
and begin the countdown.

Race two AI opponents through a forest circuit, tunnel, and tire-lined bends.
The game includes Normal/Easy difficulty, ordered checkpoints, lap timing,
standings and results, restart, cockpit/chase cameras, a rotating minimap, music,
and driving sound effects.

## Run

Use the .NET 10 SDK and a browser with WebGPU support:

```powershell
git clone --recurse-submodules https://github.com/Lminta/RaceImperialis.git
cd RaceImperialis
dotnet build RaceImperialis.slnx
dotnet run --project RaceImperialis/RaceImperialis.csproj
```

Open **https://localhost:7280**, or open `RaceImperialis.slnx` in Rider and select
**Race Imperialis: https**. The launch profile uses HTTPS only.

If the local development certificate is not trusted, run
`dotnet dev-certs https --trust`.

For an existing checkout, initialize the engine before building:

```powershell
git submodule update --init --recursive
```

## Controls

| Input | Action |
| --- | --- |
| W | Accelerate |
| A/D or left/right arrows | Steer |
| S | Brake, then reverse |
| Space | Brake |
| V | Switch cockpit/chase camera |
| Mouse | Orbit the chase camera |
| Esc | Pause and release the mouse |
| Click the canvas | Start or resume |

Changing difficulty restarts the race. The restart controls reset the grid,
opponents, timers, and countdown while retaining the selected difficulty.

## Graphics

The **Graphics → Column rendering** selector applies immediately without restarting
the race. **Every column** is the default. **Every 2nd column** and **Every 3rd column**
rotate through vertical columns each frame, retaining the other columns from
previous frames. The world is shaded into a compact half-width or third-width
target, then combined with the retained columns. This reduces actual fragment work;
fast motion can show interlacing trails. Screen overlays and the HUD update every frame.
Loading a race or changing modes first refreshes the entire scene.
FPS remains limited by the display, `targetFPS`, and CPU simulation/UI work.

Set `columnInterlaceFactor` to `1`, `2`, or `3` in
`RaceImperialis/wwwroot/graphics-settings.json` to choose the startup mode.

## Repository

- `RaceImperialis/`: application host, game rules, opponents, UI, and audio policy.
- `RaceImperialis/wwwroot/`: Pine Loop and its own maps, objects, artwork, music,
  sounds, and editable graphics/gameplay/audio settings.
- `Engine/`: the [WolfSlop Git submodule](https://github.com/Lminta/WolfSlop.git),
  including the portable engine and browser bindings referenced by this solution.
- `Tests/`: native racing integration checks and a browser driving check.
- `.run/`: shared Rider run/debug configurations.

The game builds and runs independently of the WolfNET examples repository.
Engine documentation is available in [Engine/README.md](Engine/README.md).

## Checks

```powershell
dotnet run --project Tests/RaceImperialis.Tests.csproj
```

The integration checks load this game's own assets and exercise both difficulty
profiles, countdown, driving, pause/resume, fresh race state, and ordered finishing.
The solution also includes the engine's native and public API test runners.

For the full browser check, start the game and a Chromium browser with remote
debugging enabled, then run with Node 22 or newer:

```powershell
node Tests/car-driving-browser-check.mjs 9273 https://localhost:7280
node Tests/column-interlacing-browser-check.mjs 9273 https://localhost:7280
node Tests/column-rendering-performance.mjs 9273 https://localhost:7280
```

This checks direct startup, difficulty, countdown, cockpit/chase cameras,
acceleration, steering, braking, reverse, pause/resume, restarts, and race results.
The interlacing check verifies live graphics modes, reduced ray dispatch, retained
column colors through GPU readback, and complete frames on mode changes and restarts.
The performance comparison reports whole-frame CPU time and, when supported,
GPU timestamps including history copies. It samples modes in both orders, temporarily
removes the game's FPS cap, and writes `artifacts/column-rendering/performance.json`.
The display still limits FPS. Run browser checks sequentially to preserve input focus.
Headless Edge needs `--enable-unsafe-webgpu` and
`--autoplay-policy=no-user-gesture-required`; trust the HTTPS development certificate
before testing or use `--ignore-certificate-errors` only for the test browser.
