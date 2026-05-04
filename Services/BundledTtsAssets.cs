using System.Diagnostics;

namespace LearnJP.Services;

/// <summary>
/// Reads pre-generated TTS audio files from <c>Resources/Raw/tts-assets/</c> — files that
/// were produced by the TtsPregen tool and shipped inside the app package.
///
/// Files are stored as <c>tts-assets/&lt;provider&gt;/&lt;lang&gt;/&lt;voice&gt;/&lt;wordId&gt;.&lt;ext&gt;</c>.
/// This scheme is collision-free, human-readable, and avoids the case-sensitivity pitfalls
/// of hash-based names on Android's AssetManager.
///
/// Tried extensions in order: <c>.webm</c>, <c>.mp3</c>, <c>.wav</c> — the first match wins.
/// Hits are kept in an in-process dictionary so subsequent requests for the same word serve
/// directly from memory.
/// </summary>
public sealed class BundledTtsAssets : IBundledTtsAssets
{
    private const string AssetFolder = "tts-assets";

    // null = known miss (no bundled file for that key), non-null = cached bytes.
    private readonly Dictionary<string, byte[]?> _memory = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<byte[]?> GetAsync(string provider, string voice, string lang, string wordId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(wordId)) return null;
        // Guard against path traversal — word IDs should only contain safe filename characters.
        if (wordId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || wordId.Contains(".."))
            return null;

        var memKey = $"{provider}/{lang}/{voice}/{wordId}";

        // Fast path: already resolved (hit or confirmed miss).
        if (_memory.TryGetValue(memKey, out var cached)) return cached;

        await _gate.WaitAsync(ct);
        try
        {
            if (_memory.TryGetValue(memKey, out cached)) return cached;

            var result = await TryReadFromBundleAsync(provider, lang, voice, wordId, ct);
            _memory[memKey] = result; // store null for misses so we skip the I/O next time
            return result;
        }
        finally { _gate.Release(); }
    }

    private static async Task<byte[]?> TryReadFromBundleAsync(
        string provider, string lang, string voice, string wordId, CancellationToken ct)
    {
        var folder = $"{AssetFolder}/{provider}/{lang}/{voice}";
        foreach (var ext in new[] { ".webm", ".mp3", ".wav" })
        {
            var assetPath = $"{folder}/{wordId}{ext}";
            try
            {
                await using var stream = await FileSystem.OpenAppPackageFileAsync(assetPath);
                using var ms = new MemoryStream();
                await stream.CopyToAsync(ms, ct);
                var bytes = ms.ToArray();
                if (bytes.Length > TtsCacheKey.MinAudioBytes)
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
