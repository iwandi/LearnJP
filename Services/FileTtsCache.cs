using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace LearnJP.Services;

public sealed class FileTtsCache : ITtsCache
{
    private readonly Dictionary<string, byte[]> _memory = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _dir;

    private string GetDir()
    {
        if (_dir is not null) return _dir;
        string root;
        try { root = FileSystem.AppDataDirectory; }
        catch { root = Path.Combine(Path.GetTempPath(), "LearnJP"); }
        _dir = Path.Combine(root, "tts-cache");
        try { Directory.CreateDirectory(_dir); } catch { /* ignore */ }
        return _dir;
    }

    private static string KeyFor(string provider, string voice, string lang, string text) =>
        $"{provider}|{lang}|{voice}|{text}";

    private static string FileFor(string key)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return Convert.ToHexString(bytes) + ".wav";
    }

    public async Task<byte[]?> GetAsync(string provider, string voice, string lang, string text, CancellationToken ct = default)
    {
        var key = KeyFor(provider, voice, lang, text);

        if (_memory.TryGetValue(key, out var cached)) return cached;

        var path = Path.Combine(GetDir(), FileFor(key));
        if (!File.Exists(path)) return null;

        await _gate.WaitAsync(ct);
        try
        {
            if (_memory.TryGetValue(key, out cached)) return cached;
            try
            {
                var bytes = await File.ReadAllBytesAsync(path, ct);
                _memory[key] = bytes;
                return bytes;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TtsCache] read failed for {path}: {ex.Message}");
                return null;
            }
        }
        finally { _gate.Release(); }
    }

    public async Task SetAsync(string provider, string voice, string lang, string text, byte[] data, CancellationToken ct = default)
    {
        if (data.Length == 0) return;
        var key = KeyFor(provider, voice, lang, text);
        _memory[key] = data;

        await _gate.WaitAsync(ct);
        try
        {
            try
            {
                var path = Path.Combine(GetDir(), FileFor(key));
                await File.WriteAllBytesAsync(path, data, ct);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TtsCache] write failed: {ex.Message}");
            }
        }
        finally { _gate.Release(); }
    }
}
