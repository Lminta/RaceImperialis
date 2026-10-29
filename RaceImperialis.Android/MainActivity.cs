using Android.App;
using Android.Content.PM;
using Org.Libsdl.App;

namespace RaceImperialis.Android;

[Activity(Label = "Race Imperialis", MainLauncher = true, Exported = true,
    ScreenOrientation = ScreenOrientation.SensorLandscape,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.KeyboardHidden)]
public sealed class MainActivity : SDLActivity
{
    protected override string[] GetLibraries() => ["SDL3", "SDL3_mixer"];
    protected override void Main()
    {
        var content = Path.Combine(FilesDir!.AbsolutePath, "Content");
        // SDL_mixer streams from files. Extract packaged assets into private app storage.
        CopyAssets("Content", content);
        NativeGame.Run([], content, FilesDir.AbsolutePath);
    }
    private void CopyAssets(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var name in Assets!.List(source) ?? [])
        {
            var path = source + "/" + name;
            var target = Path.Combine(destination, name);
            if ((Assets.List(path)?.Length ?? 0) > 0) CopyAssets(path, target);
            else
            {
                using var input = Assets.Open(path);
                using var output = File.Create(target);
                input.CopyTo(output);
            }
        }
    }
}
