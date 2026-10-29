using System.Diagnostics;
using System.Text.Json;

namespace RaceImperialis.Game;

// Created only with ?profile=1; no timestamp reads in ordinary game frames.
internal sealed class GameFrameProfile
{
    private static readonly string[] Stages = ["inputMs", "simulationMs", "musicAndCameraMs", "sfxMs",
        "worldMs", "hudUpdateMs", "hudRenderMs", "notificationsMs"];
    private readonly double[] _totals = new double[Stages.Length];
    private long _last, _window = Stopwatch.GetTimestamp();
    private int _frames;

    public void Begin() => _last = Stopwatch.GetTimestamp();
    public void Mark(int stage)
    {
        var now = Stopwatch.GetTimestamp();
        _totals[stage] += Stopwatch.GetElapsedTime(_last, now).TotalMilliseconds;
        _last = now;
    }
    public void End()
    {
        Mark(7);
        _frames++;
        if (Stopwatch.GetElapsedTime(_window).TotalSeconds < 1) return;
        var sample = new Dictionary<string, double> { ["frames"] = _frames };
        for (var i = 0; i < Stages.Length; i++) sample[Stages[i]] = _totals[i] / _frames;
        sample["totalMs"] = _totals.Sum() / _frames;
        Console.WriteLine("[FRAME PROFILE] " + JsonSerializer.Serialize(sample));
        Array.Clear(_totals);
        _frames = 0;
        _window = Stopwatch.GetTimestamp();
    }
}
