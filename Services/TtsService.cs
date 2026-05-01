using System.Diagnostics;
using Microsoft.Maui.Media;

namespace LearnJP.Services;

public sealed class TtsService : ITtsService
{
    private readonly ISettingsService _settings;
    private CancellationTokenSource? _cts;
    private Locale? _japaneseLocaleCache;
    private Locale? _englishLocaleCache;
    private bool _localesLoaded;
    private bool _engineDisabled;

    public TtsService(ISettingsService settings) { _settings = settings; }

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
        if (_engineDisabled) return;
        if (string.IsNullOrWhiteSpace(text)) return;

        bool ttsEnabled;
        try { ttsEnabled = _settings.TtsEnabled; } catch { ttsEnabled = false; }
        if (!ttsEnabled) return;

        // SpeechSynthesizer on Windows requires the UI thread.
        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            try
            {
                await EnsureLocalesAsync();
                try { Cancel(); } catch { /* ignore */ }
                _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

                var locale = languagePrefix == "ja" ? _japaneseLocaleCache : _englishLocaleCache;
                var options = new SpeechOptions
                {
                    Locale = locale,
                    Pitch = 1.0f,
                    Volume = 1.0f
                };

                Debug.WriteLine($"[TTS] Speaking '{text}' lang={languagePrefix} locale={locale?.Language ?? "<default>"}/{locale?.Country ?? ""} name={locale?.Name ?? ""}");
                await TextToSpeech.Default.SpeakAsync(text, options, _cts.Token);
                Debug.WriteLine("[TTS] Speak completed.");
            }
            catch (OperationCanceledException) { /* expected when interrupted */ }
            catch (System.Runtime.InteropServices.COMException ex)
            {
                Debug.WriteLine($"[TTS] COMException 0x{ex.HResult:X8}: {ex.Message}. Disabling TTS for the session.");
                _engineDisabled = true;
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
            Debug.WriteLine($"[TTS] {locales.Count} locale(s) available:");
            foreach (var l in locales)
                Debug.WriteLine($"[TTS]   - lang={l.Language} country={l.Country} name={l.Name}");

            _japaneseLocaleCache = locales.FirstOrDefault(l =>
                l.Language?.StartsWith("ja", StringComparison.OrdinalIgnoreCase) == true);
            _englishLocaleCache = locales.FirstOrDefault(l =>
                l.Language?.StartsWith("en", StringComparison.OrdinalIgnoreCase) == true);

            if (_japaneseLocaleCache is null)
                Debug.WriteLine("[TTS] No Japanese voice installed — JP playback will use the default voice.");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[TTS] GetLocalesAsync failed: {ex.GetType().Name}: {ex.Message}");
        }
        finally { _localesLoaded = true; }
    }
}
