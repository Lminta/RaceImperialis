using System.Diagnostics;
using System.Numerics;
using SDL3;
using WolfSlop.Engine.Assets;
using WolfSlop.Engine.Graphics;
using WolfSlop.Engine.Sdl.Graphics;
using WolfSlop.Engine.Sdl.Input;
using WolfSlop.Engine.Sdl.UI;

public static class NativeGame
{
    public static int Run(string[] args, string? contentRoot = null, string? preferencesRoot = null)
    {
        try
        {
            GpuDevice.Check(SDL.Init(SDL.InitFlags.Video | SDL.InitFlags.Audio | SDL.InitFlags.Gamepad));
            SDL.SetHint("SDL_TOUCH_MOUSE_EVENTS", "0");
            try
            {
                var displays = SDL.GetDisplays(out _) ?? throw new InvalidOperationException(SDL.GetError());
                var primary = SDL.GetPrimaryDisplay();
                for (var index = 0; index < displays.Length; index++)
                {
                    GpuDevice.Check(SDL.GetDisplayBounds(displays[index], out var bounds));
                    Console.WriteLine($"[DISPLAY] {index + 1}: {SDL.GetDisplayName(displays[index])}, {bounds.W}x{bounds.H} at {bounds.X},{bounds.Y}{(displays[index] == primary ? " (primary)" : "")}");
                }
                if (args.Contains("--list-displays")) return 0;
                var smoke = args.Contains("--smoke-test");
                var selectedDisplay = smoke ? displays.FirstOrDefault(display => display != primary, primary) : primary;
                if (args.FirstOrDefault(arg => arg.StartsWith("--display=")) is { } displayArgument)
                {
                    if (!int.TryParse(displayArgument[10..], out var number) || number < 1 || number > displays.Length)
                        throw new ArgumentException($"--display must be between 1 and {displays.Length}.");
                    selectedDisplay = displays[number - 1];
                }
                var assets = new DirectoryAssetSource(contentRoot ?? Path.Combine(AppContext.BaseDirectory, "Content"));
                var settings = assets.ReadJsonAsync<RendererSettings>("graphics-settings.json").GetAwaiter().GetResult(); settings.Validate();
                var gameplay = assets.ReadJsonAsync<GameplaySettings>("gameplay-settings.json").GetAwaiter().GetResult(); gameplay.Validate();
                var sound = assets.ReadJsonAsync<SoundSettings>("audio-settings.json").GetAwaiter().GetResult(); sound.Validate();
                var preferences = new GraphicsPreferences(settings, Path.Combine(preferencesRoot ??
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RaceImperialis"), "graphics.json"));
                if (!smoke) preferences.LoadAsync().GetAwaiter().GetResult();
                var window = GpuDevice.Require(SDL.CreateWindow("Race Imperialis — Pine Loop", 1280, 720,
                    SDL.WindowFlags.Resizable | SDL.WindowFlags.HighPixelDensity | SDL.WindowFlags.Hidden |
                    (smoke ? SDL.WindowFlags.NotFocusable : 0)));
                try
                {
                    var centered = unchecked((int)SDL.WindowPosCenteredDisplay((int)selectedDisplay));
                    if (!OperatingSystem.IsAndroid()) GpuDevice.Check(SDL.SetWindowPosition(window, centered, centered));
                    GpuDevice.Check(SDL.ShowWindow(window));
                    if (!OperatingSystem.IsAndroid()) GpuDevice.Check(SDL.SyncWindow(window));
                    if (!OperatingSystem.IsAndroid() && SDL.GetDisplayForWindow(window) != selectedDisplay)
                        throw new InvalidOperationException("The window manager did not place the game on the selected display.");
                    Console.WriteLine($"[DISPLAY] Running on display {Array.IndexOf(displays, selectedDisplay) + 1}: {SDL.GetDisplayName(selectedDisplay)}");
                    using var gpu = new GpuDevice(window, args.Contains("--gpu-debug"), args.FirstOrDefault(a => a.StartsWith("--gpu="))?[6..]);
                    using var renderer = new SdlSceneRenderer(gpu, assets, settings);
                    using var ui = new SdlUiRenderer(assets);
                    var input = new SdlInput(window, useRelativeMouse: !smoke);
                    using var game = new Wolf(assets, settings, gameplay, sound, renderer, ui, input);
                    game.Load(preferences);
                    var clock = Stopwatch.StartNew(); var previous = clock.Elapsed.TotalMilliseconds;
                    var quit = false; var frames = 0; var startPosition = game.Scene.Player.Transform.Position;
                    var smokeArtifacts = Path.Combine(Directory.GetCurrentDirectory(), "artifacts", "native-" + renderer.Driver);
                    double pausedTime = 0;
                    while (!quit)
                    {
                        var start = clock.Elapsed.TotalMilliseconds;
                        var elapsed = (float)(start - previous); previous = start;
                        SDL.GetWindowSize(window, out var logicalWidth, out var logicalHeight);
                        SDL.GetWindowSizeInPixels(window, out var width, out var height);
                        var viewport = new Vector2(Math.Max(1, logicalWidth), Math.Max(1, logicalHeight));
                        while (SDL.PollEvent(out var e))
                        {
                            if (e.Type == (uint)SDL.EventType.WillEnterBackground) input.Release();
                            if (e.Type is (uint)SDL.EventType.Quit or (uint)SDL.EventType.WindowCloseRequested) quit = true;
                            if (smoke) continue;
                            if (e.Type == (uint)SDL.EventType.KeyDown && !e.Key.Repeat)
                            {
                                if (e.Key.Scancode == SDL.Scancode.Escape) { if (game.MenuOpen) game.ToggleMenu(); else input.Release(); }
                                if (e.Key.Scancode == SDL.Scancode.F1) game.ToggleMenu();
                                if (e.Key.Scancode == SDL.Scancode.R && !game.MenuOpen) game.Restart();
                                if (!input.Captured) game.Document.Input.KeyDown(e.Key.Scancode switch { SDL.Scancode.Tab => "Tab", SDL.Scancode.Return => "Enter", SDL.Scancode.Space => " ", _ => "" });
                            }
                            Vector2 Pointer(float x, float y) => new(x, y);
                            if (e.Type is (uint)SDL.EventType.FingerDown or (uint)SDL.EventType.FingerMotion or (uint)SDL.EventType.FingerUp)
                            {
                                var touch = e.TFinger;
                                var point = new Vector2(touch.X, touch.Y) * viewport;
                                if (game.MenuOpen || !input.Captured)
                                {
                                    if (e.Type == (uint)SDL.EventType.FingerDown && !game.Document.Input.PointerDown(point, (int)touch.FingerID) && !game.MenuOpen) input.Capture();
                                    if (e.Type == (uint)SDL.EventType.FingerMotion) game.Document.Input.PointerMove(point, (int)touch.FingerID);
                                    if (e.Type == (uint)SDL.EventType.FingerUp) game.Document.Input.PointerUp(point, (int)touch.FingerID);
                                }
                                else if (e.Type == (uint)SDL.EventType.FingerDown && touch.X > .8f && touch.Y < .35f) game.ToggleMenu();
                                input.Touch(touch, e.Type == (uint)SDL.EventType.FingerUp);
                            }
                            if (!input.Captured && e.Type == (uint)SDL.EventType.MouseButtonDown)
                            {
                                if (!game.Document.Input.PointerDown(Pointer(e.Button.X, e.Button.Y)) && !game.MenuOpen) input.Capture();
                            }
                            if (!input.Captured && e.Type == (uint)SDL.EventType.MouseButtonUp) game.Document.Input.PointerUp(Pointer(e.Button.X, e.Button.Y));
                            if (!input.Captured && e.Type == (uint)SDL.EventType.MouseMotion) game.Document.Input.PointerMove(Pointer(e.Motion.X, e.Motion.Y));
                            if (!input.Captured && e.Type == (uint)SDL.EventType.MouseWheel) game.Document.Input.Wheel(Pointer(e.Wheel.MouseX, e.Wheel.MouseY), -e.Wheel.Y * 32);
                            input.Handle(e);
                        }
                        if (quit) break;
                        if (smoke)
                        {
                            if (frames == 2) input.Capture();
                            if (frames > 2 && frames < 480) input.Keyboard.Press("KeyW");
                            if (frames == 350)
                            {
                                if (game.Scene.PlayerRaceState?.Phase != RacePhase.Racing || Vector2.Distance(startPosition, game.Scene.Player.Transform.Position) < .1f)
                                    throw new InvalidOperationException("Native countdown did not release driving.");
                                renderer.SaveScreenshot(Path.Combine(smokeArtifacts, "cockpit.png"), ui.Bitmap);
                                input.Keyboard.Press("KeyV");
                            }
                            if (frames == 390) renderer.SaveScreenshot(Path.Combine(smokeArtifacts, "chase.png"), ui.Bitmap);
                            if (frames is 400 or 430 or 460)
                            {
                                var changed = GraphicsOptions.From(settings);
                                changed.ColumnInterlaceFactor = frames == 400 ? 2 : frames == 430 ? 3 : 1;
                                changed.Width = frames == 430 ? 800 : 960; changed.Height = frames == 430 ? 600 : 540;
                                renderer.ApplySettings(changed.ToRendererSettings(settings)); game.Scene.ApplyGraphicsSettings(settings);
                            }
                            if (frames == 480) { game.ToggleMenu(); pausedTime = game.Scene.PlayerRaceState!.Value.TotalMilliseconds; }
                            if (frames == 500)
                            {
                                if (game.Scene.PlayerRaceState!.Value.TotalMilliseconds != pausedTime) throw new InvalidOperationException("Menu did not pause the race.");
                                renderer.SaveScreenshot(Path.Combine(smokeArtifacts, "graphics.png"), ui.Bitmap);
                                game.ToggleMenu(); game.Load(preferences, RaceDifficulty.Easy);
                            }
                            elapsed = 1000f / 60;
                        }
                        game.Frame(elapsed, viewport, preferences); frames++;
                        if (smoke && frames >= 520)
                        {
                            if (game.Difficulty != RaceDifficulty.Easy || game.Scene.PlayerRaceState?.Phase != RacePhase.AwaitingStart)
                                throw new InvalidOperationException("Restart did not create a fresh Easy race.");
                            Console.WriteLine($"PASS: SDL native smoke test, {frames} frames, driving, camera, columns 1/2/3, resize, menu pause, Easy restart, GPU readback; driver={renderer.Driver}"); break;
                        }
                        var delay = 1000.0 / settings.TargetFPS - (clock.Elapsed.TotalMilliseconds - start);
                        if (delay > 1) SDL.Delay((uint)delay);
                    }
                }
                finally { SDL.DestroyWindow(window); }
            }
            finally { SDL.Quit(); }
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
