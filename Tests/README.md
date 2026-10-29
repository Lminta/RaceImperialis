# Native game checks

Run from the repository root:

```sh
dotnet run --project Tests/RaceImperialis.Tests.csproj
dotnet run --project RaceImperialis/RaceImperialis.csproj -- --smoke-test
dotnet run --project RaceImperialis/RaceImperialis.csproj -- --smoke-test --gpu=vulkan
```

Integration checks load direct filesystem assets and exercise difficulty, countdown, driving, pause, restarts, finishing, graphics validation and file-backed preferences. Native checks verify content path confinement, mud sample compensation, UI view-box/transform/clipping/color handling, actual font metrics, shader source hashes, and absence of web runtime dependencies.

GPU smoke tests open a window, execute 520 frames, validate driving and menu pause/restart, change camera/resolution/column modes, and read actual GPU output. Screenshots are saved in `artifacts/native-<driver>/`. Smoke tests default to a non-primary monitor and use non-focusable windows with synthetic input. Use `--display=2` to choose monitor 2 explicitly, or `--list-displays` to inspect the layout. Run GPU tests sequentially to avoid competing GPU load. Pure engine/UI tests remain under `Engine/Tests`.
