using System.Numerics;
using WolfSlop.Engine.Assets;
using WolfSlop.Engine.Graphics;
using WolfSlop.Engine.Sdl.Audio;
using WolfSlop.Engine.Sdl.Graphics;
using WolfSlop.Engine.Sdl.Input;
using WolfSlop.Engine.Sdl.UI;
using WolfSlop.Engine.UI;

namespace RaceImperialis.Game;

/// <summary>Native game composition. All SDL calls run on the window thread.</summary>
public sealed class Wolf(DirectoryAssetSource assets, RendererSettings settings, GameplaySettings gameplay, SoundSettings sound,
    SdlSceneRenderer renderer, SdlUiRenderer ui, SdlInput input) : IDisposable
{
    public const string DefaultMapPath = "maps/pine-loop.json";
    private GameScene? _scene;
    private ExampleHud? _hud;
    private NativeMenu? _menu;
    private MusicPlayer? _music;
    private SfxSystem? _sfx;
    private RaceDifficulty? _restart;
    private float _fpsTime;
    private int _frames, _fps;
    public GameScene Scene => _scene ?? throw new InvalidOperationException("Load a race first.");
    public UiDocument Document => _hud!.Document;
    public RaceDifficulty Difficulty { get; private set; } = gameplay.Opponents.DefaultDifficulty;
    public bool MenuOpen => _menu?.IsOpen == true;
    public void Load(GraphicsPreferences preferences, RaceDifficulty? difficulty = null)
    {
        input.Release(); _music?.Dispose(); _music = null; _sfx?.Dispose(); _sfx = null;
        _hud?.Dispose(); _scene?.Dispose(); Difficulty = difficulty ?? Difficulty;
        var loaded = new GameSceneLoader(assets, settings, gameplay).LoadAsync(DefaultMapPath).GetAwaiter().GetResult();
        renderer.PrepareSceneAsync(loaded.Map.RendererMap, resources: new(loaded.Character.AddTextures(loaded.Map.RendererMap.Textures))).GetAwaiter().GetResult();
        _scene = new(loaded.Map, loaded.Character, input, input, settings, gameplay, Difficulty); _scene.SetPaused(true);
        _hud = new(true, loaded.Map.RendererMap.GetRaceMinimap());
        _hud.RestartRequested += Restart;
        _menu = new(Document, settings, preferences, () => input.Release(), () => Difficulty, next => _restart = next,
            next => { renderer.ApplySettings(next); Scene.ApplyGraphicsSettings(settings); });
        try
        {
            if (loaded.Map.Music is { Tracks.Length: > 0 } music && sound.GetMusicVolume(music) > 0)
                _music = new(new SdlAudioBackend(assets), music, sound);
            if (sound.Volume > 0 && sound.Sfx.Volume > 0)
            {
                var effects = new SdlSfxBackend<SfxCue>(assets, sound.Sfx.Clips, new SfxVariantSelector().Select, (cue, path) => {
                    if (cue is not (SfxCue.MudSlow or SfxCue.MudMid or SfxCue.MudHigh)) return 1;
                    using var wave = assets.Open(path); return SfxClipLevels.MudGain(wave);
                });
                var gate = loaded.Map.RendererMap.GetRace()?.StartFinish.Center;
                _sfx = new(effects, sound, loaded.Map.AmbientEmitters.Where(e => e.Cue == "crowd").Select(e => e.Position).ToArray(),
                    gate == null ? loaded.Map.SpawnPosition : new(gate[0], gate[1]));
            }
        }
        catch (Exception ex) { Console.Error.WriteLine($"[AUDIO] {ex.Message}"); }
        _fpsTime = 0; _frames = 0; _restart = null;
    }
    public void ToggleMenu() { if (_menu!.IsOpen) _menu.Close(); else _menu.Open(); }
    public void Restart() => _restart = Difficulty;
    public void Frame(float milliseconds, Vector2 viewport, GraphicsPreferences preferences)
    {
        if (_restart is { } next) Load(preferences, next);
        var wasPaused = Scene.Paused; Scene.SetPaused(!input.Captured || MenuOpen);
        Scene.Update(wasPaused != Scene.Paused ? 0 : Math.Min(100, milliseconds));
        var race = Scene.PlayerRaceState;
        var volume = race is { Phase: RacePhase.Countdown } countdown ? sound.CountdownVolumeScale(countdown.CountdownProgress) : 1;
        _music?.SetPlaying(!Scene.Paused && race?.Phase is RacePhase.Countdown or RacePhase.Racing, volume); _music?.Update();
        if (race is { Phase: RacePhase.Finished } && input.Captured) input.Release();
        var frame = Scene.CaptureFrame(); _sfx?.Update(Scene.CaptureAudioFrame(frame, !Scene.Paused, milliseconds / 1000)); renderer.Render(frame);
        _frames++; _fpsTime += milliseconds;
        if (_fpsTime >= 500) { _fps = (int)MathF.Round(_frames * 1000 / _fpsTime); _fpsTime = 0; _frames = 0; }
        _hud!.Update(Scene, frame, viewport, _fps, milliseconds);
        ui.Render(Document.BuildFrame(viewport, ui)); renderer.Present(ui.Bitmap);
    }
    public void Dispose() { input.Release(); _music?.Dispose(); _sfx?.Dispose(); _hud?.Dispose(); _scene?.Dispose(); }
}
