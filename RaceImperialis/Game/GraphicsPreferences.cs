using System.ComponentModel.DataAnnotations;
using System.Text.Json;

using WolfSlop.Engine.Graphics;

namespace RaceImperialis.Game;

public sealed class GraphicsPreferences(RendererSettings settings, string path)
{
    private readonly RendererSettings _defaults = GraphicsOptions.From(settings).ToRendererSettings(settings);
    public GraphicsOptions Defaults => GraphicsOptions.From(_defaults);

    public async Task LoadAsync()
    {
        try
        {
            var json = File.Exists(path) ? await File.ReadAllTextAsync(path) : null;
            if (json == null) return;
            var saved = JsonSerializer.Deserialize<GraphicsOptions>(json, JsonSerializerOptions.Web);
            if (saved != null) settings.Apply(saved.ToRendererSettings(settings));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ValidationException or ArgumentException)
        {
            Console.WriteLine("[GRAPHICS] Saved preferences unavailable; using graphics-settings.json.");
        }
    }

    public async Task SaveAsync()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + ".tmp";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(GraphicsOptions.From(settings), JsonSerializerOptions.Web));
        File.Move(temporary, path, overwrite: true);
    }
}
