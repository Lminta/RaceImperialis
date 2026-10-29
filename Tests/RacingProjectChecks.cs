using WolfSlop.Engine.Assets;
using System.Numerics;
using WolfSlop.Engine.Graphics;
using Racing = RaceImperialis.Game;
using RacingAudio = RaceImperialis.Audio;

internal static class RacingProjectChecks
{
    public static async Task<int> RunAsync(string root)
    {
        var count = 0;
        void Check(bool value, string message) { count++; if (!value) throw new Exception(message); }
        var contentRoot = Path.Combine(root, "RaceImperialis", "Content");
        var source = new DirectoryAssetSource(contentRoot);
        var graphics = (await source.ReadJsonAsync<RendererSettings>("graphics-settings.json"))!;
        var gameplay = (await source.ReadJsonAsync<Racing.GameplaySettings>("gameplay-settings.json"))!;
        var sound = (await source.ReadJsonAsync<RacingAudio.SoundSettings>("audio-settings.json"))!;
        graphics.Validate(); gameplay.Validate(); sound.Validate();
        var loader = new Racing.GameSceneLoader(source, graphics, gameplay);
        var loaded = await loader.LoadAsync(Racing.Wolf.DefaultMapPath);
        Check(loaded.Map.Spawn.Controller == "car", "Standalone racing did not load a driving spawn.");
        Check(!typeof(Racing.GameScene).Assembly.GetReferencedAssemblies().Any(a => a.Name == "WolfSlop.Examples"),
            "Standalone racing depends on the Examples assembly.");
        var textures = loaded.Character.AddTextures(loaded.Map.RendererMap.Textures);
        var assets = textures.Layers.Values
            .Concat(loaded.Map.Music!.Tracks)
            .Concat(sound.Sfx.Clips.Values.SelectMany(paths => paths))
            .Concat(["textures/race/cockpit.png", "textures/race/steering-wheel.png"]);
        foreach (var asset in assets.OfType<string>().Distinct())
            Check(File.Exists(Path.Combine(contentRoot, asset)), $"Standalone racing is missing its own asset: {asset}");

        foreach (var difficulty in Enum.GetValues<Racing.RaceDifficulty>())
        {
            var keyboard = new FakeKeyboard { KeyState = new() { Forward = true } };
            using var scene = new Racing.GameScene(loaded.Map, loaded.Character, keyboard, new FakeMouse(), graphics, gameplay, difficulty);
            Check(scene.Player is CarObject && scene.Opponent != null && scene.SecondOpponent != null,
                "Standalone racing did not create a player car and both opponents.");
            var grid = scene.Player.Transform.Position;
            var viewSettings = new RendererSettings(1024, 768, 75) { FovDegrees = 110 };
            var raceBeforeGraphics = scene.RaceState;
            scene.ApplyGraphicsSettings(viewSettings);
            Check(MathF.Abs(scene.Player.Camera.Plane.Length() - viewSettings.CameraPlaneScale) < .0001f &&
                MathF.Abs(scene.CaptureFrame().AspectRatio - 4f / 3f) < .0001f,
                "Graphics changes did not update field of view and aspect ratio.");
            Check(scene.Player.Transform.Position == grid && scene.RaceState == raceBeforeGraphics,
                "Graphics changes reset the race or moved the player.");
            for (var frame = 0; frame < 60; frame++) scene.Update(1000f / 60);
            Check(scene.RaceState?.Phase == Racing.RacePhase.Countdown && scene.Player.Transform.Position == grid,
                "Standalone countdown did not hold the starting grid.");
            for (var frame = 0; frame < 300; frame++) scene.Update(1000f / 60);
            Check(scene.RaceState?.Phase == Racing.RacePhase.Racing && Vector2.Distance(scene.Player.Transform.Position, grid) > .1f,
                $"Standalone {difficulty} race did not release driving after the countdown.");
            scene.SetPaused(true);
            var paused = scene.Player.Transform.Position;
            var elapsed = scene.RaceState!.Value.TotalMilliseconds;
            scene.Update(200);
            Check(scene.Player.Transform.Position == paused && scene.RaceState.Value.TotalMilliseconds == elapsed,
                "Standalone racing advanced while paused.");
            scene.SetPaused(false);
            scene.Update(100);
            Check(scene.RaceState.Value.TotalMilliseconds > elapsed, "Standalone racing did not resume timing.");
        }

        using (var restart = new Racing.GameScene(loaded.Map, loaded.Character, new FakeKeyboard(), new FakeMouse(), graphics, gameplay))
            Check(restart.RaceState is { Phase: Racing.RacePhase.AwaitingStart, CompletedLaps: 0, TotalMilliseconds: 0 },
                "A fresh standalone race retained previous race progress.");

        var definition = Racing.RaceMetadata.GetRace(loaded.Map.RendererMap)!;
        var session = new Racing.RaceSession(definition, gameplay.Race);
        session.BeginCountdown();
        session.AdvanceStart(5000);
        void Cross(Racing.RaceGateDefinition gate)
        {
            var center = new Vector2(gate.Center[0], gate.Center[1]);
            var direction = Vector2.Normalize(new(gate.Direction[0], gate.Direction[1]));
            session.Update(center - direction, center + direction, 1000);
        }
        Cross(definition.StartFinish);
        for (var lap = 0; lap < gameplay.Race.LapCount; lap++)
        {
            foreach (var gate in definition.Checkpoints) Cross(gate);
            Cross(definition.StartFinish);
        }
        Check(session.Status.Phase == Racing.RacePhase.Finished && session.Status.LapTimes.Count == gameplay.Race.LapCount,
            "Standalone Pine Loop did not finish after the configured ordered laps.");
        return count;
    }
}
