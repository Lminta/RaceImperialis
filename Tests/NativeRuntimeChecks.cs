using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using SkiaSharp;
using WolfSlop.Engine.Assets;
using WolfSlop.Engine.Sdl.UI;
using WolfSlop.Engine.UI;

internal static class NativeRuntimeChecks
{
    public static int Run(string root)
    {
        var count = 0;
        void Check(bool value, string message) { count++; if (!value) throw new Exception(message); }
        var assets = new DirectoryAssetSource(Path.Combine(root, "RaceImperialis", "Content"));
        using (var wave = assets.Open("sounds/mud-mid-1.wav"))
            Check(RaceImperialis.Audio.SfxClipLevels.MudGain(wave) > 1, "Native mud playback lost its level compensation.");
        foreach (var path in new[] { "../outside.json", "maps/../../outside.json" })
        {
            try { assets.Open(path); throw new Exception("Content traversal was allowed."); }
            catch (InvalidDataException) { count++; }
        }
        Check(AssetSourceExtensions.RelativeTo("maps/track.json", "../objects/car.json") == "maps/../objects/car.json", "Relative map object path changed.");
        foreach (var assembly in new[] { typeof(RaceImperialis.Game.Wolf).Assembly, typeof(SdlUiRenderer).Assembly })
            Check(!assembly.GetReferencedAssemblies().Any(reference => reference.Name!.StartsWith("Microsoft.AspNetCore") ||
                reference.Name.StartsWith("Microsoft.JSInterop") || reference.Name.StartsWith("SpawnDev")), "Native runtime retains a web dependency.");

        using var renderer = new SdlUiRenderer(assets);
        var drawing = new UiDrawingBuilder().Rectangle(new(0, 0, 5, 5), "#ff000080").Build();
        renderer.Render(new(1, 100, 100, [new UiRenderNode {
            Id = "transformed", Kind = "canvas", Width = 100, Height = 100,
            Transform = UiMatrix.From(Matrix3x2.CreateTranslation(10, 10)), ViewBox = new(0, 0, 50, 50),
            DrawingTransform = UiMatrix.From(Matrix3x2.CreateTranslation(5, 0)), Drawing = drawing,
            Clips = [new(new(0, 0, 25, 100), UiMatrix.Identity)] }], null, null));
        var pixel = renderer.Bitmap.GetPixel(22, 12);
        Check(pixel.Red > 240 && pixel.Alpha is >= 127 and <= 129, "Native UI lost RGBA color, view-box scaling, or transform order.");
        Check(renderer.Bitmap.GetPixel(12, 12).Alpha == 0, "Native drawing translation was ignored.");
        Check(renderer.Bitmap.GetPixel(27, 12).Alpha == 0, "Native UI ignored clipping.");
        Check(renderer.MeasureWidth("WWW", new(Size: 20)) > renderer.MeasureWidth("iii", new(Size: 20)), "Native UI uses approximate font metrics.");

        var shaders = Path.Combine(root, "Engine", "WolfSlop.Engine.Sdl", "Shaders");
        var manifest = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(shaders, "Compiled", "sources.json")))!;
        foreach (var (file, hash) in manifest)
            Check(Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(File.ReadAllText(Path.Combine(shaders, file)).Replace("\r\n", "\n")))) == hash,
                $"Shader '{file}' changed without regenerating native binaries.");
        return count;
    }
}
