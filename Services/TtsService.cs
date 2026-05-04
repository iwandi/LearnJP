using System.Diagnostics;
using LearnJP.Models;
using Microsoft.Maui.Media;

namespace LearnJP.Services;

/// <summary>
/// Provider-agnostic, language-neutral TTS. The active language pack supplies the BCP-47
/// locale and the provider-specific voice; this service holds no per-language state of its
/// own and never inspects <see cref="Word"/> fields. Anything language-specific must arrive
/// either from the pack data or from a <see cref="LanguageBehavior"/>.
/// </summary>
public sealed class TtsService : ITtsService
{
    private readonly ISettingsService _settings;
    private readonly ISoundService _sounds;
    private readonly AzureTtsClient _azure;
    private readonly ILanguagePackService _packs;

    private CancellationTokenSource? _cts;
    // Keyed by BCP-47 tag so that switching language packs doesn't reuse another language's locale.
    private readonly Dictionary<string, Locale?> _systemLocales = new(StringComparer.OrdinalIgnoreCase);
    private bool _systemEngineDisabled;

    // Texts already submitted for prefetch. Reset on language change so the cache can warm
    // again for the new locale instead of short-circuiting on stale entries.
    private readonly HashSet<string> _prefetched = new(StringComparer.Ordinal);

    public TtsService(ISettingsService settings, ISoundService sounds, AzureTtsClient azure, ILanguagePackService packs)
    {
        _settings = settings;
        _sounds = sounds;
        _azure = azure;
        _packs = packs;
        _packs.ActiveChanged += (_, _) =>
        {
            lock (_prefetched) _prefetched.Clear();
        };
    }

    public Task SpeakAsync(string text, string? wordId = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text)) return Task.CompletedTask;
        if (!IsEnabled()) return Task.CompletedTask;
        if (!TryResolveTarget(out var locale, out var voice)) return Task.CompletedTask;

        return CurrentProvider() switch
        {
            TtsProvider.Azure => SpeakAzureAsync(text, locale, voice, wordId, ct),
            _                 => SpeakSystemAsync(text, locale, ct)
        };
    }

    public void CancelSpeaking()
    {
        try { _cts?.Cancel(); } catch { /* ignore */ }
    }

    public async Task PrefetchAsync(IEnumerable<(string wordId, string text)> items, CancellationToken ct = default)
    {
        if (items is null) return;
        if (!IsEnabled()) return;
        // Only Azure has a persistent cache to warm; system TTS goes straight to the OS engine.
        if (CurrentProvider() != TtsProvider.Azure) return;
        if (!TryResolveTarget(out var locale, out var voice)) return;

        foreach (var (wordId, text) in items)
        {
            if (ct.IsCancellationRequested) return;
            if (string.IsNullOrWhiteSpace(text)) continue;
            lock (_prefetched)
            {
                if (!_prefetched.Add(text)) continue;
            }
            try { await _azure.SynthesizeAsync(text, locale, voice, wordId, ct); }
            catch { /* best effort */ }
        }
    }

    private bool IsEnabled()
    {
        try { return _settings.TtsEnabled; } catch { return false; }
    }

    private TtsProvider CurrentProvider()
    {
        try { return _settings.TtsProvider; } catch { return TtsProvider.System; }
    }

    /// <summary>Pulls the BCP-47 locale and the provider-specific voice off the active pack.
    /// Returns false (and skips synthesis) if no pack is active or no locale is configured.</summary>
    private bool TryResolveTarget(out string locale, out string voice)
    {
        locale = string.Empty;
        voice = string.Empty;
        var pack = _packs.Active;
        if (pack is null) return false;
        if (string.IsNullOrWhiteSpace(pack.TtsLocale)) return false;
        locale = pack.TtsLocale;
        voice = pack.GetVoiceFor(CurrentProvider());
        return true;
    }

    private async Task SpeakAzureAsync(string text, string locale, string voice, string? wordId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(voice)) return; // Azure requires an explicit voice name.

        var wav = await _azure.SynthesizeAsync(text, locale, voice, wordId, ct);
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

    private async Task SpeakSystemAsync(string text, string locale, CancellationToken ct)
    {
        if (_systemEngineDisabled) return;
        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            try
            {
                var sysLocale = await ResolveSystemLocaleAsync(locale);
                CancelSpeaking();
                _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

                var sysVolume = 1.0;
                try { sysVolume = _settings.SystemTtsVolume; } catch { /* default */ }
                var options = new SpeechOptions { Locale = sysLocale, Pitch = 1.0f, Volume = (float)Math.Clamp(sysVolume, 0.0, 1.0) };

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

    /// <summary>Maps a BCP-47 tag (e.g. "ja-JP") to a system <see cref="Locale"/> the OS engine
    /// understands. Match the language prefix only ("ja") because some platforms expose
    /// "ja-Jpan" or no region tag at all. Cached per language so we don't query each speak.</summary>
    private async Task<Locale?> ResolveSystemLocaleAsync(string bcp47)
    {
        var langPrefix = (bcp47.Split('-').FirstOrDefault() ?? string.Empty).ToLowerInvariant();
        if (string.IsNullOrEmpty(langPrefix)) return null;

        if (_systemLocales.TryGetValue(langPrefix, out var cached)) return cached;
        try
        {
            var locales = (await TextToSpeech.Default.GetLocalesAsync()).ToList();
            var match = locales.FirstOrDefault(l =>
                l.Language?.StartsWith(langPrefix, StringComparison.OrdinalIgnoreCase) == true);
            _systemLocales[langPrefix] = match;
            return match;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[TTS] GetLocalesAsync failed: {ex.GetType().Name}: {ex.Message}");
            _systemLocales[langPrefix] = null;
            return null;
        }
    }

    /// <summary>Returns the approximate duration in milliseconds of an audio buffer, or 0 on
    /// parse failure. Handles RIFF/WAV (exact, via header), MP3 (approximate at 96 kbps),
    /// and WebM/OGG (approximate at 64 kbps — the Opus rate Azure uses).</summary>
    private static int EstimateWavDurationMs(byte[] audio)
    {
        try
        {
            if (audio.Length < 8) return 0;

            // RIFF/WAV: parse the standard header for an exact value.
            if (audio[0] == 'R' && audio[1] == 'I' && audio[2] == 'F' && audio[3] == 'F'
                && audio.Length >= 44)
            {
                var sampleRate = BitConverter.ToUInt32(audio, 24);
                var channels   = BitConverter.ToUInt16(audio, 22);
                var bps        = BitConverter.ToUInt16(audio, 34);
                var dataBytes  = BitConverter.ToUInt32(audio, 40);
                if (sampleRate == 0 || channels == 0 || bps == 0) return 0;
                var bytesPerSecond = sampleRate * channels * (bps / 8u);
                if (bytesPerSecond == 0) return 0;
                return (int)(dataBytes * 1000 / bytesPerSecond);
            }

            if (audio.Length <= 64) return 0;

            // WebM (starts with 0x1A 0x45 0xDF 0xA3 — EBML magic) or OGG (starts with "OggS"):
            // Azure Opus streams are ~64 kbps. duration_ms ≈ bytes * 8 / 64 = bytes / 8
            if ((audio[0] == 0x1A && audio[1] == 0x45 && audio[2] == 0xDF && audio[3] == 0xA3)
                || (audio[0] == 'O' && audio[1] == 'g' && audio[2] == 'g' && audio[3] == 'S'))
            {
                return audio.Length / 8;
            }

            // Fallback: assume MP3 at 96 kbps (the rate Azure uses for audio-24khz-96kbitrate-mono-mp3).
            // Unknown or unsupported formats will produce an imprecise estimate, but that is
            // acceptable — the caller only needs a rough delay so speech isn't cut off early.
            // duration_ms = bytes * 8 / kbps = bytes * 8 / 96 = bytes / 12
            return audio.Length / 12;
        }
        catch { return 0; }
    }
}
