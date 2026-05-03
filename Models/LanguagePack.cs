using System.Text.Json.Serialization;

namespace LearnJP.Models;

/// <summary>
/// Per-language configuration. Defines how the algorithm and UI should treat words from a
/// specific target language. Loaded from <c>Resources/Raw/Languages/&lt;id&gt;.json</c>.
/// Vocabulary files are still language-shaped (kanji/kana/romaji) for now — the shape will
/// generalise in a later step. Until then <see cref="VocabFile"/> just points at the file
/// to load and the algorithm reads the existing <see cref="Word"/> fields directly.
/// </summary>
public sealed class LanguagePack
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>BCP-47 tag handed to the TTS engine, e.g. "ja-JP".</summary>
    [JsonPropertyName("ttsLocale")]
    public string TtsLocale { get; set; } = string.Empty;

    /// <summary>Azure neural voice used when the user hasn't picked a custom voice.</summary>
    [JsonPropertyName("defaultAzureVoice")]
    public string DefaultAzureVoice { get; set; } = string.Empty;

    /// <summary>Vocabulary file to load (relative to Resources/Raw).</summary>
    [JsonPropertyName("vocabFile")]
    public string VocabFile { get; set; } = "vocabulary.json";

    /// <summary>
    /// Tags identifying single-glyph entries (e.g. hiragana characters in Japanese, jamo in
    /// Korean) that should be excluded from general practice unless explicitly opted in.
    /// </summary>
    [JsonPropertyName("glyphTags")]
    public List<string> GlyphTags { get; set; } = new();
}

/// <summary>
/// Index of bundled language packs. <see cref="ILanguagePackService"/> loads this file and
/// then opens each referenced pack file. We ship a manifest because MAUI's asset bundle
/// model doesn't expose a portable directory-enumeration API.
/// </summary>
public sealed class LanguagePackIndex
{
    [JsonPropertyName("packs")]
    public List<string> Packs { get; set; } = new();
}
