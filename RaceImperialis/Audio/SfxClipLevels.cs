namespace RaceImperialis.Audio;

/// <summary>Playback compensation for quiet generated mud recordings; source files stay intact.</summary>
public static class SfxClipLevels
{
    public static float MudGain(Stream wave)
    {
        using var reader = new BinaryReader(wave, System.Text.Encoding.UTF8, leaveOpen: true);
        if (new string(reader.ReadChars(4)) != "RIFF") throw new InvalidDataException("Expected RIFF audio.");
        reader.ReadUInt32();
        if (new string(reader.ReadChars(4)) != "WAVE") throw new InvalidDataException("Expected WAVE audio.");
        ushort channels = 0, bits = 0, format = 0;
        while (wave.Position + 8 <= wave.Length)
        {
            var chunk = new string(reader.ReadChars(4)); var size = reader.ReadUInt32(); var next = wave.Position + size + (size & 1);
            if (chunk == "fmt ")
            {
                format = reader.ReadUInt16(); channels = reader.ReadUInt16();
                reader.ReadUInt32(); reader.ReadUInt32(); reader.ReadUInt16(); bits = reader.ReadUInt16();
            }
            else if (chunk == "data")
            {
                if (format != 1 || bits != 16 || channels == 0) throw new InvalidDataException("Mud recordings require 16-bit PCM WAVE.");
                var length = checked((int)(size / (channels * 2)));
                var samples = Enumerable.Range(0, channels).Select(_ => new float[length]).ToArray();
                for (var i = 0; i < length; i++) for (var channel = 0; channel < channels; channel++) samples[channel][i] = reader.ReadInt16() / 32768f;
                return MudGain(samples);
            }
            wave.Position = next;
        }
        throw new InvalidDataException("WAVE file has no sample data.");
    }
    public static float MudGain(IReadOnlyList<float[]> channels)
    {
        if (channels.Count == 0 || channels[0].Length == 0) return 1;
        var length = channels[0].Length;
        if (channels.Any(channel => channel.Length != length))
            throw new ArgumentException("Audio channels must have equal lengths.", nameof(channels));
        double energy = 0, peak = 0;
        for (var i = 0; i < length; i++)
        {
            double mono = 0;
            foreach (var channel in channels) mono += channel[i];
            mono /= channels.Count;
            if (!double.IsFinite(mono)) return 1;
            energy += mono * mono;
            peak = Math.Max(peak, Math.Abs(mono));
        }
        var rms = Math.Sqrt(energy / length);
        if (rms < .000001 || peak == 0) return 1;
        // Aim for -18 dBFS RMS after stereo downmix. Limit boosts to +30 dB
        // and leave 10% peak headroom instead of clipping transient splashes.
        return (float)Math.Clamp(Math.Min(.12589254 / rms, .9 / peak), 1, 32);
    }
}
