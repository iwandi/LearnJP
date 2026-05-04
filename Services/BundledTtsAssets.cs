using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace LearnJP.Services;

/// <summary>
/// Reads pre-generated TTS audio files from <c>Resources/Raw/tts-assets/</c> — files that
/// were produced by the TtsPregen tool and shipped inside the app package.
///
/// File names are SHA-256 hashes of the cache key (<c>provider|lang|voice|text</c>), matching
/// the scheme used by <see cref="FileTtsCache"/> and the TtsPregen output tool so the same
/// hash identifies the same audio regardless of where it lives.
///
/// Tried extensions in order: <c>.webm</c>, <c>.mp3</c>, <c>.wav</c> — the first match wins.
/// Hits are kept in an in-process dictionary so subsequent requests for the same text serve
/// directly from memory.
/// </summary>
public sealed class BundledTtsAssets : IBundledTtsAssets
{
    private const string AssetFolder = "tts-assets";

    // null = known miss (no bundled file for that key), non-null = cached bytes.
    private readonly Dictionary<string, byte[]?> _memory = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _gate = new(1, 1);

    private static string KeyFor(string provider, string voice, string lang, string text) =>
        $"{provider}|{lang}|{voice}|{text}";

    private static string HashHex(string key)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return Convert.ToHexString(bytes);
    }

    public async Task<byte[]?> GetAsync(string provider, string voice, string lang, string text, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var key = KeyFor(provider, voice, lang, text);

        // Fast path: already resolved (hit or confirmed miss).
        if (_memory.TryGetValue(key, out var cached)) return cached;

        await _gate.WaitAsync(ct);
        try
        {
            if (_memory.TryGetValue(key, out cached)) return cached;

            var result = await TryReadFromBundleAsync(HashHex(key), ct);
            _memory[key] = result; // store null for misses so we skip the I/O next time
            return result;
        }
        finally { _gate.Release(); }
    }

    private static async Task<byte[]?> TryReadFromBundleAsync(string hashHex, CancellationToken ct)
    {
        foreach (var ext in new[] { ".webm", ".mp3", ".wav" })
        {
            var assetPath = $"{AssetFolder}/{hashHex}{ext}";
            try
            {
                await using var stream = await FileSystem.OpenAppPackageFileAsync(assetPath);
                using var ms = new MemoryStream();
                await stream.CopyToAsync(ms, ct);
                var bytes = ms.ToArray();
                if (bytes.Length > 64)
                {
                    Debug.WriteLine($"[BundledTtsAssets] hit: {assetPath} ({bytes.Length} bytes)");
                    return bytes;
                }
            }
            catch (FileNotFoundException) { /* expected for missing assets */ }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Debug.WriteLine($"[BundledTtsAssets] read error for {assetPath}: {ex.Message}");
            }
        }
        return null;
    }
}
