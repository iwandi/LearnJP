using System.Security.Cryptography;
using System.Text;

namespace LearnJP.Services;

/// <summary>
/// Shared helpers for computing TTS cache keys and file names.
/// The key format <c>provider|lang|voice|text</c> (and its SHA-256 hex hash) is used by
/// both <see cref="FileTtsCache"/> and <see cref="BundledTtsAssets"/> — keeping this in one
/// place ensures the two implementations never drift apart.
/// </summary>
internal static class TtsCacheKey
{
    /// <summary>Minimum byte length that is considered a valid audio payload.
    /// Responses shorter than this are treated as synthesis failures.</summary>
    internal const int MinAudioBytes = 64;

    /// <summary>Builds the canonical cache key string for the given synthesis parameters.</summary>
    internal static string For(string provider, string voice, string lang, string text) =>
        $"{provider}|{lang}|{voice}|{text}";

    /// <summary>Returns the uppercase hex SHA-256 digest of the given cache key.</summary>
    internal static string HashHex(string key)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return Convert.ToHexString(bytes);
    }
}
