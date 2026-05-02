using System.Diagnostics;
using LearnJP.Models;
using Microsoft.Maui.Media;

namespace LearnJP.Services;

public sealed class TtsService : ITtsService
{
    private readonly ISettingsService _settings;
    private readonly ISoundService _sounds;
    private readonly AzureTtsClient _azure;

    private CancellationTokenSource? _cts;
    private Locale? _japaneseLocaleCache;
    private Locale? _englishLocaleCache;
    private bool _localesLoaded;
    private bool _systemEngineDisabled;

    public TtsService(ISettingsService settings, ISoundService sounds, AzureTtsClient azure)
    {
        _settings = settings;
        _sounds = sounds;
        _azure = azure;
    }

    public Task SpeakJapaneseAsync(string text, CancellationToken ct = default) =>
        SpeakAsync(text, "ja", ct);

    public Task SpeakEnglishAsync(string text, CancellationToken ct = default) =>
        SpeakAsync(text, "en", ct);

    public void Cancel()
    {
        try { _cts?.Cancel(); } catch { /* ignore */ }
    }

    private async Task SpeakAsync(string text, string languagePrefix, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        bool ttsEnabled;
        try { ttsEnabled = _settings.TtsEnabled; } catch { ttsEnabled = false; }
        if (!ttsEnabled) return;

        TtsProvider provider;
        try { provider = _settings.TtsProvider; } catch { provider = TtsProvider.System; }

        switch (provider)
        {
            case TtsProvider.Azure:
                await SpeakAzureAsync(text, languagePrefix, ct);
                break;
            default:
                if (_systemEngineDisabled) return;
                await SpeakSystemAsync(text, languagePrefix, ct);
                break;
        }
    }

    private async Task SpeakAzureAsync(string text, string languagePrefix, CancellationToken ct)
    {
        var (lang, voice) = languagePrefix == "ja"
            ? ("ja-JP", _settings.AzureJapaneseVoice)
            : ("en-US", _settings.AzureEnglishVoice);

        var wav = await _azure.SynthesizeAsync(text, lang, voice, ct);
        if (wav is null || wav.Length < 64) return;

        var volume = 1.0;
        try { volume = _settings.AzureTtsVolume; } catch { /* default */ }
        _sounds.PlayWav(wav, volume);
        // PlayWav is fire-and-forget on Windows; wait the WAV's own duration so callers awaiting
        // SpeakAsync don't return before the audio actually finishes.
        var dur = EstimateWavDurationMs(wav);
        if (dur > 0)
        {
            try { await Task.Delay(TimeSpan.FromMilliseconds(dur), ct); }
            catch (OperationCanceledException) { /* expected on cancel */ }
        }
    }

    private async Task SpeakSystemAsync(string text, string languagePrefix, CancellationToken ct)
    {
        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            try
            {
                await EnsureLocalesAsync();
                try { Cancel(); } catch { /* ignore */ }
                _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

                var locale = languagePrefix == "ja" ? _japaneseLocaleCache : _englishLocaleCache;
                var sysVolume = 1.0;
                try { sysVolume = _settings.SystemTtsVolume; } catch { /* default */ }
                var options = new SpeechOptions { Locale = locale, Pitch = 1.0f, Volume = (float)Math.Clamp(sysVolume, 0.0, 1.0) };

                await TextToSpeech.Default.SpeakAsync(text, options, _cts.Token);
            }
            catch (OperationCanceledException) { /* expected */ }
            catch (System.Runtime.InteropServices.COMException ex)
            {
                Debug.WriteLine($"[TTS] COMException 0x{ex.HResult:X8}: {ex.Message}. Disabling TTS for the session.");
                _systemEngineDisabled = true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TTS] {ex.GetType().Name}: {ex.Message}");
            }
        });
    }

    private async Task EnsureLocalesAsync()
    {
        if (_localesLoaded) return;
        try
        {
            var locales = (await TextToSpeech.Default.GetLocalesAsync()).ToList();
            _japaneseLocaleCache = locales.FirstOrDefault(l =>
                l.Language?.StartsWith("ja", StringComparison.OrdinalIgnoreCase) == true);
            _englishLocaleCache = locales.FirstOrDefault(l =>
                l.Language?.StartsWith("en", StringComparison.OrdinalIgnoreCase) == true);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[TTS] GetLocalesAsync failed: {ex.GetType().Name}: {ex.Message}");
        }
        finally { _localesLoaded = true; }
    }

    /// <summary>Returns the duration of a canonical PCM WAV in milliseconds, or 0 on parse failure.</summary>
    private static int EstimateWavDurationMs(byte[] wav)
    {
        try
        {
            if (wav.Length < 44) return 0;
            // sampleRate at offset 24 (little-endian uint32), channels at 22 (uint16), bitsPerSample at 34 (uint16),
            // data chunk size at offset 40 (uint32). This is the canonical RIFF/WAV layout Azure returns.
            var sampleRate = BitConverter.ToUInt32(wav, 24);
            var channels   = BitConverter.ToUInt16(wav, 22);
            var bps        = BitConverter.ToUInt16(wav, 34);
            var dataBytes  = BitConverter.ToUInt32(wav, 40);
            if (sampleRate == 0 || channels == 0 || bps == 0) return 0;
            var bytesPerSecond = sampleRate * channels * (bps / 8u);
            if (bytesPerSecond == 0) return 0;
            return (int)(dataBytes * 1000 / bytesPerSecond);
        }
        catch { return 0; }
    }
}
