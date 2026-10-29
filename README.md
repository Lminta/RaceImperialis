# Race Imperialis

A Windows .NET 10 racing game using SDL3 and SDL GPU. Race two opponents through Pine Loop with cockpit/chase cameras, lap timing, ordered checkpoints, difficulty settings, music, spatial sound, and a retained UI HUD.

## Run on Windows

```sh
git clone --recurse-submodules https://github.com/Lminta/RaceImperialis.git
cd RaceImperialis
dotnet run --project RaceImperialis/RaceImperialis.csproj
```

The Windows build uses SDL3 and selects D3D12 or Vulkan. A compatible GPU and driver and the .NET 10 SDK are required. The default runtime target is `win-x64`.

Click the game to start or resume. W accelerates; S brakes then reverses; A/D or arrows steer; Space brakes; V changes camera; mouse movement orbits the chase camera. Esc pauses and releases the mouse. R restarts; F1 opens graphics settings. Menus also support Tab and Enter.

The graphics menu provides resolution presets, FPS cap, filtering, field of view, column rendering, render distance, and fog. Click a value to cycle choices. Apply saves a JSON file under the user's local application-data directory in `RaceImperialis/graphics.json`. Cancel preserves active settings. Defaults come from `Content/graphics-settings.json`.

Column modes 2 and 3 update alternating columns in a persistent world target. Compute rays and expensive world shading skip other columns; the HUD is composited every frame. Fast motion can show trails. The native implementation uses a full-size retained target; performance depends on the GPU and UI workload.

## Publish the Windows build

```sh
dotnet publish RaceImperialis/RaceImperialis.csproj -c Release -r win-x64 --self-contained true -o artifacts/publish/win-x64
```

Distribute the complete output directory, including `Content` and native libraries.

## Projects

- `RaceImperialis/`: Windows entry point, game source, and `Content/` assets.
- `RaceImperialis/Core/`: game library used by the Windows application and tests.
- `Engine/`: WolfSlop submodule with a portable core and native SDL provider.
- `Tests/`: game, asset, preferences, native UI, and shader-consistency checks.

The engine owns simulation and provider contracts. `WolfSlop.Engine.Sdl` owns SDL GPU, native input, SDL_mixer playback, image decoding, and SkiaSharp UI rasterization. HLSL sources and precompiled SPIR-V/DXIL live in the provider; shader compilers are build tools only.

## Checks

```sh
dotnet build RaceImperialis.slnx
dotnet run --project Tests/RaceImperialis.Tests.csproj
dotnet run --project Engine/Tests/EngineTests/WolfSlop.EngineTests.csproj
dotnet run --project Engine/Tests/EngineConsumer/WolfSlop.EngineConsumer.csproj
dotnet run --project RaceImperialis/RaceImperialis.csproj -- --smoke-test
dotnet run --project RaceImperialis/RaceImperialis.csproj -- --smoke-test --gpu=vulkan
```

The GPU smoke test opens a real window and exercises countdown/driving, cameras, column modes, resolution changes, menu pause, Easy restart, and GPU readback. It exits automatically and saves screenshots in `artifacts/native-<driver>/`. Smoke tests automatically use the first non-primary monitor when available. Their windows do not take focus or capture the mouse/keyboard; driving uses synthetic input. Run GPU tests sequentially to avoid competing GPU load. `--display=2` explicitly chooses the second monitor (numbered from 1); `--list-displays` lists monitor names and bounds. `--gpu-debug` enables SDL GPU validation; `--gpu=direct3d12` or `--gpu=vulkan` selects a backend.

After modifying HLSL, rebuild both shader formats:

```sh
dotnet run --project Engine/Tools/ShaderCompiler/ShaderCompiler.csproj
```

Commit the updated sources, binaries, and source hash manifest together. The shader compiler tool requires Windows SDL_shadercross binaries supplied by its NuGet packages.
