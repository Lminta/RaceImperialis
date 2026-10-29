# Race Imperialis

A native .NET 10 racing game using SDL3 and SDL GPU. Race two opponents through Pine Loop with cockpit/chase cameras, lap timing, ordered checkpoints, difficulty settings, music, spatial sound, and a retained UI HUD.

## Run on Windows or Linux

```sh
git clone --recurse-submodules https://github.com/Lminta/RaceImperialis.git
cd RaceImperialis
dotnet run --project RaceImperialis/RaceImperialis.csproj
```

The native provider selects D3D12 or Vulkan. A compatible GPU and driver are required. On Linux, install your distribution's Vulkan loader/driver and the desktop/audio libraries needed by SDL. The packaged Linux native libraries require glibc 2.28 or newer.

Click the game to start or resume. W accelerates; S brakes then reverses; A/D or arrows steer; Space brakes; V changes camera; mouse movement orbits the chase camera. Esc pauses and releases the mouse. R restarts; F1 opens graphics settings. Menus also support Tab and Enter.

The graphics menu provides resolution presets, FPS cap, filtering, field of view, column rendering, render distance, and fog. Click a value to cycle choices. Apply saves a JSON file under the user's local application-data directory in `RaceImperialis/graphics.json`. Cancel preserves active settings. Defaults come from `Content/graphics-settings.json`.

Column modes 2 and 3 update alternating columns in a persistent world target. Compute rays and expensive world shading skip other columns; the HUD is composited every frame. Fast motion can show trails. The native implementation uses a full-size retained target; performance depends on the GPU and UI workload.

## Publish desktop builds

```sh
dotnet publish RaceImperialis/RaceImperialis.csproj -c Release -r win-x64 --self-contained true -o artifacts/publish/win-x64
dotnet publish RaceImperialis/RaceImperialis.csproj -c Release -r linux-x64 --self-contained true -o artifacts/publish/linux-x64
```

Distribute the complete output directory, including `Content` and native libraries.

## Android

```sh
dotnet workload install android
dotnet build RaceImperialis.Android/RaceImperialis.Android.csproj
```

Android also needs the Android SDK and JDK. The separate project creates an APK; it stays outside the desktop solution so desktop builds do not require mobile tooling. The activity uses landscape orientation and extracts bundled content into private app storage for native streaming audio. No network permission is needed.

Tap to start/resume. The lower screen is split into left steering, right steering, brake/reverse, and acceleration regions. Tap the upper-left region to change camera and upper-right to open the menu. Backgrounding releases controls and pauses the race. Android uses Vulkan with optional SDL GPU features disabled and filtering limited to 1× for wider driver compatibility. A Vulkan-capable device is required; minimum Android version alone does not guarantee compatible graphics.

Windows D3D12 and Vulkan have been exercised locally. Linux publishing and Android APK building are checked; Linux runtime and Android device behavior still need testing. The SDL Android package currently produces duplicate `libSDL3.so` packaging warnings; the SDK retains one copy per ABI.

## Projects

- `RaceImperialis/`: desktop entry point, game source, and `Content/` assets.
- `RaceImperialis/Core/`: shared game library used by desktop, Android, and tests.
- `RaceImperialis.Android/`: Android activity and packaging.
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

Commit the updated sources, binaries, and source hash manifest together. The shader compiler tool requires desktop SDL_shadercross binaries supplied by its NuGet packages.
