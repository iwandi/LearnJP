using System.Diagnostics;
using System.Net.Http;
using System.Text;

namespace LearnJP.Services;

public sealed class AzureTtsClient : IDisposable
{
    private const string ProviderName = "azure";

    private readonly ISettingsService _settings;
    private readonly ITtsCache _cache;
    private readonly HttpClient _http;

    public AzureTtsClient(ISettingsService settings, ITtsCache cache)
    {
        _settings = settings;
        _cache = cache;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
    }

    /// <summary>
    /// Returns a complete RIFF/WAV byte buffer (24kHz, 16-bit, mono PCM) for the given text,
    /// served from cache when available. Returns null on any synthesis failure.
    /// </summary>
    public async Task<byte[]?> SynthesizeAsync(string text, string languageTag, string voiceName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var cached = await _cache.GetAsync(ProviderName, voiceName, languageTag, text, ct);
        if (cached is { Length: > 64 }) return cached;

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
            req.Headers.Add("X-Microsoft-OutputFormat", "audio-24khz-96kbitrate-mono-mp3");
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
            if (bytes.Length > 64)
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
