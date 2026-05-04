using System.Diagnostics;
using Plugin.Maui.Audio;

namespace LearnJP.Services;

public sealed class SoundService : ISoundService
{
    private readonly IAudioManager _audioManager;
    private readonly object _gate = new();
    private readonly Dictionary<SoundEffect, byte[]> _wavCache = new();
    private readonly Dictionary<SoundEffect, EffectPlayer> _effectPlayers = new();
    private readonly HashSet<TransientPlayer> _activeTransients = new();

    public SoundService(IAudioManager audioManager) { _audioManager = audioManager; }

    /// <summary>Standard RIFF/WAV header size in bytes.</summary>
    private const int WavHeaderSize = 44;

    // Sounds loaded from Resources/Raw/sounds/ (correct.wav, wrong.wav).
    // Written once by PreloadAsync before Play is first called; read without a lock because
    // the reference assignment is atomic and PreloadAsync completes before Play is ever called.
    private volatile Dictionary<SoundEffect, byte[]>? _rawSounds;

    private static readonly IReadOnlyDictionary<SoundEffect, string> RawSoundFiles =
        new Dictionary<SoundEffect, string>
        {
            { SoundEffect.Correct, "sounds/correct.wav" },
            { SoundEffect.Wrong,   "sounds/wrong.wav"   },
        };

    public async Task PreloadAsync()
    {
        var loaded = new Dictionary<SoundEffect, byte[]>();
        foreach (var (effect, filename) in RawSoundFiles)
        {
            try
            {
                await using var stream = await FileSystem.OpenAppPackageFileAsync(filename);
                using var ms = new MemoryStream();
                await stream.CopyToAsync(ms);
                var bytes = ms.ToArray();
                if (bytes.Length > WavHeaderSize) // sanity-check: must be longer than the WAV header
                    loaded[effect] = bytes;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Sound] PreloadAsync({filename}) skipped: {ex.Message}");
            }
        }
        _rawSounds = loaded;
    }

    public TimeSpan Play(SoundEffect effect)
    {
        try
        {
            var ep = GetOrCreateEffectPlayer(effect);
            if (ep is not null)
            {
                try { if (ep.Player.IsPlaying) ep.Player.Stop(); } catch { /* ignore */ }
                try { ep.Player.Volume = 1.0; } catch { /* ignore */ }
                ep.Player.Play();
            }
        }
        catch (Exception ex) { Debug.WriteLine($"[Sound] Play({effect}) failed: {ex.Message}"); }
        return DurationOf(effect);
    }

    public void PlayWav(byte[] wavBytes, double volume = 1.0)
    {
        if (wavBytes is null || wavBytes.Length == 0) return;
        try
        {
            // Each WAV gets its own player + stream; releasing the player tears down the stream too.
            var stream = new MemoryStream(wavBytes, writable: false);
            var player = _audioManager.CreatePlayer(stream);
            try { player.Volume = Math.Clamp(volume, 0.0, 1.0); } catch { /* ignore */ }

            var transient = new TransientPlayer(player, stream);
            lock (_gate) _activeTransients.Add(transient);

            player.PlaybackEnded += (_, _) => Cleanup(transient);
            player.Play();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Sound] PlayWav failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void Cleanup(TransientPlayer t)
    {
        // Dispose() can synchronously raise PlaybackEnded on some platforms — guard against re-entry.
        if (Interlocked.Exchange(ref t.Cleaned, 1) != 0) return;
        lock (_gate) _activeTransients.Remove(t);
        try { t.Player.Dispose(); } catch { /* ignore */ }
        try { t.Stream.Dispose(); } catch { /* ignore */ }
    }

    private EffectPlayer? GetOrCreateEffectPlayer(SoundEffect effect)
    {
        lock (_gate)
        {
            if (_effectPlayers.TryGetValue(effect, out var existing)) return existing;
            try
            {
                var wav = GetOrBuildWav(effect);
                var stream = new MemoryStream(wav, writable: false);
                var player = _audioManager.CreatePlayer(stream);
                var ep = new EffectPlayer(player, stream);
                _effectPlayers[effect] = ep;
                return ep;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Sound] CreatePlayer({effect}) failed: {ex.GetType().Name}: {ex.Message}");
                return null;
            }
        }
    }

    private byte[] GetOrBuildWav(SoundEffect effect)
    {
        if (_wavCache.TryGetValue(effect, out var cached)) return cached;

        // Prefer the sound loaded from Resources/Raw/sounds/ so users can swap files.
        var rawSounds = _rawSounds;
        if (rawSounds is not null && rawSounds.TryGetValue(effect, out var rawWav))
        {
            _wavCache[effect] = rawWav;
            return rawWav;
        }

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

    private static TimeSpan DurationOf(SoundEffect effect) => effect switch
    {
        SoundEffect.Click   => TimeSpan.FromMilliseconds(60),
        SoundEffect.Correct => TimeSpan.FromMilliseconds(200),
        SoundEffect.Wrong   => TimeSpan.FromMilliseconds(300),
        _                   => TimeSpan.FromMilliseconds(80)
    };

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
        const int header = WavHeaderSize;
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

        using var ms = new MemoryStream(WavHeaderSize + dataSize);
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

    private sealed record EffectPlayer(IAudioPlayer Player, MemoryStream Stream);

    private sealed class TransientPlayer
    {
        public IAudioPlayer Player { get; }
        public MemoryStream Stream { get; }
        public int Cleaned;
        public TransientPlayer(IAudioPlayer player, MemoryStream stream)
        { Player = player; Stream = stream; }
    }
}
