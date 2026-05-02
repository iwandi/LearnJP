using System.Diagnostics;
using System.Net.Http;
using System.Text;

namespace LearnJP.Services;

public sealed class AzureTtsClient : IDisposable
{
    private readonly ISettingsService _settings;
    private readonly HttpClient _http;

    public AzureTtsClient(ISettingsService settings)
    {
        _settings = settings;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
    }

    /// <summary>
    /// Returns a complete RIFF/WAV byte buffer (24kHz, 16-bit, mono PCM) for the given text,
    /// or null on any failure (missing key, network, throttling, etc.).
    /// </summary>
    public async Task<byte[]?> SynthesizeAsync(string text, string languageTag, string voiceName, CancellationToken ct = default)
    {
        var key = _settings.AzureSpeechKey;
        var region = _settings.AzureSpeechRegion;
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(region))
        {
            Debug.WriteLine("[AzureTTS] Missing key or region — Azure TTS not configured.");
            return null;
        }

        var endpoint = $"https://{region}.tts.speech.microsoft.com/cognitiveservices/v1";

        // SSML must escape XML-significant characters in the inner text.
        var safeText = System.Security.SecurityElement.Escape(text) ?? text;
        var ssml =
            $"<speak version='1.0' xml:lang='{languageTag}'>" +
              $"<voice xml:lang='{languageTag}' name='{voiceName}'>{safeText}</voice>" +
            "</speak>";

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, endpoint);
            req.Headers.Add("Ocp-Apim-Subscription-Key", key);
            req.Headers.Add("X-Microsoft-OutputFormat", "riff-24khz-16bit-mono-pcm");
            req.Headers.Add("User-Agent", "LearnJP/1.0");
            req.Content = new StringContent(ssml, Encoding.UTF8, "application/ssml+xml");

            using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseContentRead, ct);
            if (!resp.IsSuccessStatusCode)
            {
                var body = await SafeReadAsStringAsync(resp, ct);
                Debug.WriteLine($"[AzureTTS] HTTP {(int)resp.StatusCode} {resp.StatusCode}: {body}");
                return null;
            }

            return await resp.Content.ReadAsByteArrayAsync(ct);
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
