using System.Diagnostics;

#if WINDOWS
using System.Runtime.InteropServices;
#endif

namespace LearnJP.Services;

public sealed class SoundService : ISoundService
{
    private readonly object _gate = new();
    private readonly Dictionary<SoundEffect, byte[]> _wavCache = new();

#if WINDOWS
    private const uint SND_ASYNC  = 0x0001;
    private const uint SND_MEMORY = 0x0004;
    private const uint SND_NODEFAULT = 0x0002;

    [DllImport("winmm.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool PlaySound(byte[]? lpszSound, IntPtr hModule, uint dwFlags);
#endif

    public TimeSpan Play(SoundEffect effect)
    {
        try
        {
#if WINDOWS
            PlayWav(GetOrBuild(effect));
#else
            _ = effect;
#endif
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Sound] {ex.GetType().Name}: {ex.Message}");
        }
        return DurationOf(effect);
    }

    private static TimeSpan DurationOf(SoundEffect effect) => effect switch
    {
        SoundEffect.Click   => TimeSpan.FromMilliseconds(60),
        SoundEffect.Correct => TimeSpan.FromMilliseconds(200),
        SoundEffect.Wrong   => TimeSpan.FromMilliseconds(300),
        _                   => TimeSpan.FromMilliseconds(80)
    };

    public void PlayWav(byte[] wavBytes, double volume = 1.0)
    {
#if WINDOWS
        if (wavBytes.Length == 0) return;
        var clamped = Math.Clamp(volume, 0.0, 1.0);
        var toPlay = clamped >= 0.999 ? wavBytes : ScaleWav16BitPcm(wavBytes, clamped);
        _ = Task.Run(() =>
        {
            try { PlaySound(toPlay, IntPtr.Zero, SND_MEMORY | SND_ASYNC | SND_NODEFAULT); }
            catch (Exception ex) { Debug.WriteLine($"[Sound] PlaySound failed: {ex.Message}"); }
        });
#else
        _ = wavBytes; _ = volume;
#endif
    }

    /// <summary>
    /// Returns a fresh WAV byte buffer with the audio data section scaled by <paramref name="volume"/>.
    /// Walks the RIFF chunk list to locate the "data" chunk so non-canonical headers are handled.
    /// </summary>
    private static byte[] ScaleWav16BitPcm(byte[] wav, double volume)
    {
        var copy = (byte[])wav.Clone();
        if (copy.Length < 44) return copy;
        // RIFF header: bytes 0..11 ("RIFF" size "WAVE"). Chunks start at offset 12.
        int p = 12;
        int dataStart = -1;
        int dataSize = 0;
        while (p + 8 <= copy.Length)
        {
            var id = System.Text.Encoding.ASCII.GetString(copy, p, 4);
            var size = BitConverter.ToInt32(copy, p + 4);
            if (id == "data") { dataStart = p + 8; dataSize = size; break; }
            p += 8 + size + (size & 1); // chunks are word-aligned
        }
        if (dataStart < 0) return copy;
        int end = Math.Min(copy.Length, dataStart + dataSize);
        for (int i = dataStart; i + 1 < end; i += 2)
        {
            short sample = BitConverter.ToInt16(copy, i);
            int scaled = (int)Math.Round(sample * volume);
            if (scaled > short.MaxValue) scaled = short.MaxValue;
            else if (scaled < short.MinValue) scaled = short.MinValue;
            copy[i]     = (byte)(scaled & 0xFF);
            copy[i + 1] = (byte)((scaled >> 8) & 0xFF);
        }
        return copy;
    }

    private byte[] GetOrBuild(SoundEffect effect)
    {
        lock (_gate)
        {
            if (_wavCache.TryGetValue(effect, out var cached)) return cached;
            var wav = effect switch
            {
                SoundEffect.Click   => BuildTone(880, 60,  0.30, 0.005),
                SoundEffect.Correct => BuildTwoTone(660, 990, 90, 110, 0.45),
                SoundEffect.Wrong   => BuildTwoTone(360, 200, 120, 180, 0.45),
                _ => BuildTone(440, 80, 0.3, 0.005)
            };
            _wavCache[effect] = wav;
            return wav;
        }
    }

    private static byte[] BuildTwoTone(int hz1, int hz2, int ms1, int ms2, double amp)
    {
        var samplesA = ExtractMonoSamples(BuildTone(hz1, ms1, amp, 0.005));
        var samplesB = ExtractMonoSamples(BuildTone(hz2, ms2, amp, 0.005));
        var combined = new short[samplesA.Length + samplesB.Length];
        Buffer.BlockCopy(samplesA, 0, combined, 0, samplesA.Length * sizeof(short));
        Buffer.BlockCopy(samplesB, 0, combined, samplesA.Length * sizeof(short), samplesB.Length * sizeof(short));
        return PackWav(combined, sampleRate: 44100);
    }

    private static short[] ExtractMonoSamples(byte[] wav)
    {
        const int header = 44;
        var len = (wav.Length - header) / 2;
        var samples = new short[len];
        Buffer.BlockCopy(wav, header, samples, 0, len * sizeof(short));
        return samples;
    }

    private static byte[] BuildTone(double frequencyHz, int durationMs, double amplitude, double fadeSec)
    {
        const int sampleRate = 44100;
        var totalSamples = sampleRate * durationMs / 1000;
        var samples = new short[totalSamples];
        var fadeSamples = (int)(sampleRate * fadeSec);
        var twoPiF = 2.0 * Math.PI * frequencyHz;

        for (int i = 0; i < totalSamples; i++)
        {
            double env = 1.0;
            if (i < fadeSamples) env = (double)i / fadeSamples;
            else if (i > totalSamples - fadeSamples) env = (double)(totalSamples - i) / fadeSamples;

            var t = (double)i / sampleRate;
            var sample = Math.Sin(twoPiF * t) * amplitude * env;
            samples[i] = (short)(sample * short.MaxValue);
        }

        return PackWav(samples, sampleRate);
    }

    private static byte[] PackWav(short[] samples, int sampleRate)
    {
        const short channels = 1;
        const short bitsPerSample = 16;
        var byteRate = sampleRate * channels * bitsPerSample / 8;
        var dataSize = samples.Length * sizeof(short);

        using var ms = new MemoryStream(44 + dataSize);
        using var bw = new BinaryWriter(ms);
        bw.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
        bw.Write(36 + dataSize);
        bw.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
        bw.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
        bw.Write(16);
        bw.Write((short)1);
        bw.Write(channels);
        bw.Write(sampleRate);
        bw.Write(byteRate);
        bw.Write((short)(channels * bitsPerSample / 8));
        bw.Write(bitsPerSample);
        bw.Write(System.Text.Encoding.ASCII.GetBytes("data"));
        bw.Write(dataSize);
        var bytes = new byte[dataSize];
        Buffer.BlockCopy(samples, 0, bytes, 0, dataSize);
        bw.Write(bytes);
        bw.Flush();
        return ms.ToArray();
    }
}
