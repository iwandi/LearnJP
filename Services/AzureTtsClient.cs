using System.Diagnostics;
using System.Net.Http;
using System.Text;

namespace LearnJP.Services;

public sealed class AzureTtsClient : IDisposable
{
    private const string ProviderName = "azure";

    private readonly ISettingsService _settings;
    private readonly IBundledTtsAssets _bundled;
    private readonly ITtsCache _cache;
    private readonly HttpClient _http;

    public AzureTtsClient(ISettingsService settings, IBundledTtsAssets bundled, ITtsCache cache)
    {
        _settings = settings;
        _bundled  = bundled;
        _cache    = cache;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
    }

    /// <summary>
    /// Returns audio bytes for the given text. Lookup order:
    /// 1. Bundled app assets (pre-generated, shipped with the app) — looked up by word ID
    ///    when <paramref name="wordId"/> is provided, skipped otherwise.
    /// 2. Mutable file cache (synthesized at runtime and persisted)
    /// 3. Azure TTS API (live synthesis; result is stored in the file cache)
    /// Returns null on any synthesis failure.
    /// </summary>
    public async Task<byte[]?> SynthesizeAsync(string text, string languageTag, string voiceName, string? wordId = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        // 1. Bundled assets — fastest, no network or writable-storage I/O.
        //    Requires a word ID; arbitrary/synthesised text has no bundled file.
        if (!string.IsNullOrWhiteSpace(wordId))
        {
            var bundled = await _bundled.GetAsync(ProviderName, voiceName, languageTag, wordId, ct);
            if (bundled is { Length: > TtsCacheKey.MinAudioBytes }) return bundled;
        }

        // 2. Mutable file cache — previously synthesized audio stored on device.
        var cached = await _cache.GetAsync(ProviderName, voiceName, languageTag, text, ct);
        if (cached is { Length: > TtsCacheKey.MinAudioBytes }) return cached;

        var key = _settings.AzureSpeechKey;
        var region = _settings.AzureSpeechRegion;
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(region))
        {
            Debug.WriteLine("[AzureTTS] Missing key or region — Azure TTS not configured.");
            return null;
        }

        var endpoint = $"https://{region}.tts.speech.microsoft.com/cognitiveservices/v1";
        var safeText = System.Security.SecurityElement.Escape(text) ?? text;
        var ssml =
            $"<speak version='1.0' xml:lang='{languageTag}'>" +
              $"<voice xml:lang='{languageTag}' name='{voiceName}'>{safeText}</voice>" +
            "</speak>";

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, endpoint);
            req.Headers.Add("Ocp-Apim-Subscription-Key", key);
            req.Headers.Add("X-Microsoft-OutputFormat", "audio-24khz-96kbitrate-mono-mp3"); // MP3: universally supported on all platforms including iOS/macOS
            req.Headers.Add("User-Agent", "LearnJP/1.0");
            req.Content = new StringContent(ssml, Encoding.UTF8, "application/ssml+xml");

            using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseContentRead, ct);
            if (!resp.IsSuccessStatusCode)
            {
                var body = await SafeReadAsStringAsync(resp, ct);
                Debug.WriteLine($"[AzureTTS] HTTP {(int)resp.StatusCode} {resp.StatusCode}: {body}");
                return null;
            }

            var bytes = await resp.Content.ReadAsByteArrayAsync(ct);
            if (bytes.Length > TtsCacheKey.MinAudioBytes)
                await _cache.SetAsync(ProviderName, voiceName, languageTag, text, bytes, ct);
            return bytes;
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AzureTTS] {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    private static async Task<string> SafeReadAsStringAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        try { return await resp.Content.ReadAsStringAsync(ct); }
        catch { return "<unreadable response body>"; }
    }

    public void Dispose() => _http.Dispose();
}
