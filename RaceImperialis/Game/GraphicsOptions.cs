using System.ComponentModel.DataAnnotations;
using WolfNET.Engine.Graphics;

namespace RaceImperialis.Game;

public sealed class GraphicsOptions : IValidatableObject
{
    [Range(160, 7680, ErrorMessage = "Width must be between 160 and 7680 pixels.")]
    public int Width { get; set; }
    [Range(90, 4320, ErrorMessage = "Height must be between 90 and 4320 pixels.")]
    public int Height { get; set; }
    [Range(15, 1000, ErrorMessage = "Frame-rate limit must be between 15 and 1000 FPS.")]
    public int TargetFPS { get; set; }
    [Range(1, 16, ErrorMessage = "Anisotropic filtering must be between 1× and 16×.")]
    public ushort MaxAnisotropy { get; set; }
    [Range(1, 3, ErrorMessage = "Choose every column, every 2nd column, or every 3rd column.")]
    public int ColumnInterlaceFactor { get; set; }
    [Range(1d, 179d, ErrorMessage = "Field of view must be between 1 and 179 degrees.")]
    public float FovDegrees { get; set; }
    [Range(1d, 1024d, ErrorMessage = "Render distance must be between 1 and 1024 map squares.")]
    public float RenderDistance { get; set; }
    [Range(0d, 1023d, ErrorMessage = "Fog start must be between 0 and 1023 map squares.")]
    public float FogStartDistance { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (!float.IsFinite(FovDegrees) || !float.IsFinite(RenderDistance) || !float.IsFinite(FogStartDistance))
            yield return new("Graphics values must be finite numbers.");
        if (FogStartDistance >= RenderDistance)
            yield return new("Fog must start before the render distance.", [nameof(FogStartDistance)]);
    }

    public static GraphicsOptions From(RendererSettings settings) => new()
    {
        Width = settings.Width, Height = settings.Height, TargetFPS = settings.TargetFPS,
        MaxAnisotropy = settings.MaxAnisotropy, ColumnInterlaceFactor = settings.ColumnInterlaceFactor,
        FovDegrees = settings.FovDegrees, RenderDistance = settings.RenderDistance, FogStartDistance = settings.FogStartDistance
    };

    public RendererSettings ToRendererSettings(RendererSettings? capacities = null)
    {
        Validator.ValidateObject(this, new(this), validateAllProperties: true);
        var settings = new RendererSettings(Width, Height, TargetFPS)
        {
            MaxAnisotropy = MaxAnisotropy, ColumnInterlaceFactor = ColumnInterlaceFactor,
            FovDegrees = FovDegrees, RenderDistance = RenderDistance, FogStartDistance = FogStartDistance,
            MaxMapWidth = capacities?.MaxMapWidth ?? 512, MaxMapHeight = capacities?.MaxMapHeight ?? 512
        };
        settings.Validate();
        return settings;
    }
}
