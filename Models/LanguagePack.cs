using System.Text.Json;
using System.Text.Json.Serialization;

namespace LearnJP.Models;

/// <summary>
/// Per-language configuration. Defines how the algorithm and UI should treat words from a
/// specific target language. Loaded from <c>Resources/Raw/Languages/&lt;id&gt;.json</c>.
///
/// The pack also owns the shape of <see cref="Word.Forms"/>: it declares which JSON keys
/// in the vocabulary file should be loaded into the per-word forms array, and in what order.
/// Slot 0 is the "primary / universal" form (the only slot most languages need). Languages
/// with multiple writing systems (e.g. Japanese: kanji + kana + romaji) declare extra slots
/// after that and rely on <see cref="LanguageBehavior"/> to map roles to indices.
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

    /// <summary>
    /// Default voice per TTS provider, keyed by the provider's lowercase name
    /// (e.g. "azure"). Lets each pack ship its own preferred voice without forcing the
    /// settings layer to know about every provider/language combination.
    /// </summary>
    [JsonPropertyName("providerVoices")]
    public Dictionary<string, string> ProviderVoices { get; set; } = new();

    /// <summary>Returns the configured voice for <paramref name="provider"/>, or empty if none.
    /// Case-insensitive — System.Text.Json drops a custom comparer when deserialising, so we
    /// walk the entries explicitly instead of relying on the dictionary's own comparer.</summary>
    public string GetVoiceFor(TtsProvider provider)
    {
        var key = provider.ToString();
        foreach (var (k, v) in ProviderVoices)
            if (string.Equals(k, key, StringComparison.OrdinalIgnoreCase))
                return v ?? string.Empty;
        return string.Empty;
    }

    /// <summary>
    /// Ordered list of JSON property names in the vocabulary file that get loaded into
    /// <see cref="Word.Forms"/>. The order also defines the role→index lookup used by
    /// <see cref="LanguageBehavior"/>. Index 0 is the universal/primary form. When omitted
    /// the loader falls back to <c>["kanji"]</c>, which keeps existing single-form packs
    /// working without explicit configuration.
    /// </summary>
    [JsonPropertyName("forms")]
    public List<string> Forms { get; set; } = new();

    /// <summary>Effective form-key list (falls back to a sensible default if the pack didn't
    /// declare any). Keep going through this rather than <see cref="Forms"/> so loader and
    /// behaviour agree on the same slots.</summary>
    [JsonIgnore]
    public IReadOnlyList<string> EffectiveForms =>
        Forms.Count > 0 ? Forms : (_defaultForms ??= new[] { "kanji" });
    private static IReadOnlyList<string>? _defaultForms;

    /// <summary>Index of the named form in <see cref="EffectiveForms"/>, or -1 if absent.
    /// Behaviours use this to translate role names (e.g. "kana") into <see cref="Word.Forms"/>
    /// indices. Case-insensitive.</summary>
    public int IndexOfForm(string formKey)
    {
        var forms = EffectiveForms;
        for (int i = 0; i < forms.Count; i++)
            if (string.Equals(forms[i], formKey, StringComparison.OrdinalIgnoreCase))
                return i;
        return -1;
    }

    /// <summary>Vocabulary file to load (relative to Resources/Raw). Holds target-language
    /// fields. Translations live in <see cref="TranslationFile"/>.</summary>
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
    /// Ordered progression ladder for this language. Stage 0 is always unlocked. Each
    /// subsequent stage unlocks when the fraction of <see cref="WordProficiency.IsKnown"/>
    /// words in the previous stage reaches the stage's <see cref="ProgressionStage.UnlockThreshold"/>.
    /// When empty, <see cref="TagFilterMode.AutoProgression"/> behaves identically to
    /// <see cref="TagFilterMode.NoFilter"/>.
    /// </summary>
    [JsonPropertyName("progression")]
    public List<ProgressionStage> Progression { get; set; } = new();

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
    /// once the pack is loaded. Not serialised. The behaviour holds a back-reference to this
    /// pack so it can resolve role→index lookups.</summary>
    [JsonIgnore]
    public LanguageBehavior Behavior { get; set; } = new GenericBehavior(null);
}

/// <summary>Index of bundled language packs. <see cref="LearnJP.Services.ILanguagePackService"/>
/// loads this file and then opens each referenced pack file. We ship a manifest because MAUI's
/// asset bundle model doesn't expose a portable directory-enumeration API.</summary>
public sealed class LanguagePackIndex
{
    [JsonPropertyName("packs")]
    public List<string> Packs { get; set; } = new();
}

/// <summary>Declarative description of a per-language display toggle (e.g. JP exposes
/// "romaji-only mode" and "always show furigana"). The settings layer treats these
/// generically: it stores a bool per (packId, key) without knowing what the keys mean.</summary>
public sealed record DisplayOption(string Key, string Display, bool DefaultValue = false);

/// <summary>Read-only view of the active pack's display flags. Behaviours consult this when
/// deciding which form to render and how. Decoupled from <see cref="LearnJP.Services.ISettingsService"/>
/// so behaviours don't take a settings dependency.</summary>
public interface IDisplayFlags
{
    bool Get(string key, bool defaultValue = false);
}

/// <summary>Result of <see cref="LanguageBehavior.RenderPrompt"/>. <c>Furigana</c> is null for
/// languages / display modes that don't surface a separate reading line.</summary>
public readonly record struct PromptRender(string Prompt, string? Furigana);
