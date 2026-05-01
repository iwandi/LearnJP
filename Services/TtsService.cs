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
            await TextToSpeech.Default.SpeakAsync(text, options, _cts.Token);
        }
        catch (OperationCanceledException) { /* expected */ }
        catch (System.Runtime.InteropServices.COMException)
        {
            // Speech engine COM class not registered (e.g. unpackaged / sandbox). Disable for the session.
            _engineDisabled = true;
        }
        catch
        {
            // Any other failure — disable to avoid repeated noise.
            _engineDisabled = true;
        }
    }

    private async Task EnsureLocalesAsync()
    {
        if (_localesLoaded) return;
        try
        {
            var locales = await TextToSpeech.Default.GetLocalesAsync();
            _japaneseLocaleCache = locales.FirstOrDefault(l =>
                l.Language?.StartsWith("ja", StringComparison.OrdinalIgnoreCase) == true);
            _englishLocaleCache = locales.FirstOrDefault(l =>
                l.Language?.StartsWith("en", StringComparison.OrdinalIgnoreCase) == true);
        }
        catch { /* leave both caches null; SpeakAsync will use default locale */ }
        finally { _localesLoaded = true; }
    }
}
