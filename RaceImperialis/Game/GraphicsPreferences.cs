using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.JSInterop;
using WolfNET.Engine.Graphics;

namespace RaceImperialis.Game;

public sealed class GraphicsPreferences(IJSRuntime js, RendererSettings settings)
{
    private const string StorageKey = "race-imperialis.graphics.v1";
    private readonly RendererSettings _defaults = GraphicsOptions.From(settings).ToRendererSettings(settings);
    public GraphicsOptions Defaults => GraphicsOptions.From(_defaults);

    public async Task LoadAsync()
    {
        try
        {
            var json = await js.InvokeAsync<string?>("localStorage.getItem", StorageKey);
            if (json == null) return;
            var saved = JsonSerializer.Deserialize<GraphicsOptions>(json, JsonSerializerOptions.Web);
            if (saved != null) settings.Apply(saved.ToRendererSettings(settings));
        }
        catch (Exception ex) when (ex is JSException or JsonException or ValidationException or ArgumentException)
        {
            Console.WriteLine("[GRAPHICS] Saved preferences unavailable; using graphics-settings.json.");
        }
    }

    public async Task SaveAsync() => await js.InvokeVoidAsync("localStorage.setItem", StorageKey,
        JsonSerializer.Serialize(GraphicsOptions.From(settings), JsonSerializerOptions.Web));
}
