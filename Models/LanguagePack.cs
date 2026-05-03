using System.Text.Json;
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

    /// <summary>Vocabulary file to load (relative to Resources/Raw). Holds target-language
    /// fields (writing, reading, transliteration). Translations live in <see cref="TranslationFile"/>.</summary>
    [JsonPropertyName("vocabFile")]
    public string VocabFile { get; set; } = "vocabulary.json";

    /// <summary>
    /// Per-base-language meanings files keyed by base-language code (e.g. "en", "de").
    /// Each value is a vocabulary_&lt;target&gt;-&lt;base&gt;.json file containing
    /// <c>{ id, meanings }</c> entries. The user's selected base language picks one.
    /// </summary>
    [JsonPropertyName("translations")]
    public Dictionary<string, string> Translations { get; set; } = new();

    /// <summary>Legacy single-file fallback when <see cref="Translations"/> is empty.</summary>
    [JsonPropertyName("translationFile")]
    public string TranslationFile { get; set; } = string.Empty;

    /// <summary>Resolve which translation file to load for the requested base language.
    /// Prefers exact match, then "en", then any first entry, then the legacy field.</summary>
    public string? ResolveTranslationFile(string? baseLanguageId)
    {
        if (Translations.Count > 0)
        {
            if (!string.IsNullOrWhiteSpace(baseLanguageId)
                && Translations.TryGetValue(baseLanguageId, out var match))
                return match;
            if (Translations.TryGetValue("en", out var en)) return en;
            return Translations.Values.FirstOrDefault();
        }
        return string.IsNullOrWhiteSpace(TranslationFile) ? null : TranslationFile;
    }

    /// <summary>
    /// Tags identifying single-glyph entries (e.g. hiragana characters in Japanese, jamo in
    /// Korean) that should be excluded from general practice unless explicitly opted in.
    /// </summary>
    [JsonPropertyName("glyphTags")]
    public List<string> GlyphTags { get; set; } = new();

    /// <summary>
    /// Behavior module the algorithm should call into for language-specific decisions.
    /// Resolved via switch-case (see <see cref="LanguageBehavior.Create"/>). Defaults to
    /// <see cref="LanguageBehaviorType.Generic"/> when omitted or unrecognised.
    /// </summary>
    [JsonPropertyName("behaviorType")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public LanguageBehaviorType BehaviorType { get; set; } = LanguageBehaviorType.Generic;

    /// <summary>Free-form configuration handed to the behavior constructor. Each behavior
    /// type defines its own schema; left as a raw <see cref="JsonElement"/> so we don't
    /// pre-bind the shape here.</summary>
    [JsonPropertyName("behaviorConfig")]
    public JsonElement? BehaviorConfig { get; set; }

    /// <summary>Runtime behaviour instance, populated by <see cref="LearnJP.Services.LanguagePackService"/>
    /// once the pack is loaded. Not serialised.</summary>
    [JsonIgnore]
    public LanguageBehavior Behavior { get; set; } = new GenericBehavior(null);
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
