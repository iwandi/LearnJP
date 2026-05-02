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

    public void Play(SoundEffect effect)
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
    }

    public void PlayWav(byte[] wavBytes)
    {
#if WINDOWS
        if (wavBytes.Length == 0) return;
        _ = Task.Run(() =>
        {
            try { PlaySound(wavBytes, IntPtr.Zero, SND_MEMORY | SND_ASYNC | SND_NODEFAULT); }
            catch (Exception ex) { Debug.WriteLine($"[Sound] PlaySound failed: {ex.Message}"); }
        });
#else
        _ = wavBytes;
#endif
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
