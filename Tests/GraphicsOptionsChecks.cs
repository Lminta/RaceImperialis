using System.ComponentModel.DataAnnotations;
using System.Text.Json;

using RaceImperialis.Game;
using WolfSlop.Engine.Graphics;

internal static class GraphicsOptionsChecks
{
    public static async Task<int> RunAsync()
    {
        var count = 0;
        void Check(bool value, string message) { count++; if (!value) throw new Exception(message); }
        var initial = new RendererSettings(640, 360, 30) { FovDegrees = 90, MaxMapWidth = 1024 };
        var directory = Path.Combine(Path.GetTempPath(), "race-preferences-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "graphics.json");
        var preferences = new GraphicsPreferences(initial, path);
        var draft = GraphicsOptions.From(initial);
        foreach (var preset in GraphicsOptions.ResolutionPresets)
        {
            Check(draft.SelectResolution(preset.Value), "A resolution preset could not be selected.");
            Check(draft.Width == preset.Width && draft.Height == preset.Height, "Resolution preset dimensions do not match.");
        }
        var lastResolution = (draft.Width, draft.Height);
        Check(!draft.SelectResolution("123x456") && (draft.Width, draft.Height) == lastResolution,
            "Free-form resolution input changed the draft.");
        draft.Width = 800; draft.Height = 600; draft.TargetFPS = 75;
        draft.MaxAnisotropy = 8; draft.ColumnInterlaceFactor = 3;
        draft.FovDegrees = 110; draft.RenderDistance = 80; draft.FogStartDistance = 55;
        initial.Apply(draft.ToRendererSettings(initial));
        await preferences.SaveAsync();
        var restored = new RendererSettings(1920, 1080, 60) { MaxMapWidth = 1024 };
        await new GraphicsPreferences(restored, path).LoadAsync();
        Check(restored.Width == 800 && restored.Height == 600 && restored.TargetFPS == 75, "Saved display settings were lost.");
        Check(restored.MaxAnisotropy == 8 && restored.ColumnInterlaceFactor == 3 && restored.FovDegrees == 110,
            "Saved filtering, columns, or FOV were lost.");
        Check(restored.RenderDistance == 80 && restored.FogStartDistance == 55 && restored.MaxMapWidth == 1024,
            "Saved distances or map capacities were lost.");
        Check(preferences.Defaults.Width == 640 && preferences.Defaults.TargetFPS == 30, "Defaults changed when settings were applied.");
        preferences.Defaults.Width = 900;
        Check(preferences.Defaults.Width == 640, "Editing a default draft changed the saved defaults.");

        foreach (var invalidate in new Action<GraphicsOptions>[] {
            value => value.Width = 0, value => value.Height = 5000, value => value.TargetFPS = 0,
            value => value.MaxAnisotropy = 17, value => value.ColumnInterlaceFactor = 4,
            value => value.FovDegrees = float.NaN, value => value.RenderDistance = 0,
            value => value.FogStartDistance = value.RenderDistance })
        {
            var invalid = GraphicsOptions.From(restored); invalidate(invalid);
            try { invalid.ToRendererSettings(); throw new Exception("Invalid graphics draft was accepted."); }
            catch (ValidationException) { count++; }
        }
        try { restored.Apply(new(123, 456, 0)); throw new Exception("Invalid settings were applied."); }
        catch (ArgumentException) { count++; }
        Check(restored.Width == 800 && restored.TargetFPS == 75, "A failed update partially changed active settings.");

        await File.WriteAllTextAsync(path, "not JSON");
        await preferences.LoadAsync();
        Check(initial.Width == 800, "Malformed saved settings replaced active settings.");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new GraphicsOptions(), JsonSerializerOptions.Web));
        await preferences.LoadAsync();
        Check(initial.Width == 800, "Invalid saved values replaced active settings.");
        File.Delete(path); Directory.CreateDirectory(path);
        await preferences.LoadAsync();
        Check(initial.Width == 800, "Unavailable settings storage prevented startup.");
        Directory.Delete(path); Directory.Delete(directory);
        return count;
    }

}
