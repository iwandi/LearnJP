using System.Text.Json;

namespace LearnJP.Models;

/// <summary>
/// Identifies which concrete <see cref="LanguageBehavior"/> implementation a pack uses.
/// Resolved by a switch-case factory (no reflection) so the set of behaviours is closed
/// and visible at a glance — adding one means adding an enum value and a switch arm.
/// </summary>
public enum LanguageBehaviorType
{
    Generic,
    Japanese
}

/// <summary>
/// Per-language hooks the algorithm calls into. The base class supplies sensible defaults
/// that read the existing <see cref="Word"/> fields directly; subclasses override only what
/// they need (e.g. <see cref="JapaneseBehavior"/> tightens glyph detection to id prefixes).
/// </summary>
public abstract class LanguageBehavior
{
    /// <summary>True when the entry represents a single character / atomic glyph (e.g. one
    /// hiragana). These are excluded from general practice unless explicitly opted in.</summary>
    public virtual bool IsGlyphEntry(Word w, IReadOnlyList<string> glyphTags) =>
        glyphTags.Any(t => w.Tags.Contains(t, StringComparer.OrdinalIgnoreCase));

    /// <summary>Primary written form to display as the prompt (e.g. kanji for JP).</summary>
    public virtual string PrimaryForm(Word w) =>
        string.IsNullOrEmpty(w.Kanji) ? w.Kana : w.Kanji;

    /// <summary>Reading aid (e.g. kana / furigana). Null when not applicable.</summary>
    public virtual string? ReadingForm(Word w) => string.IsNullOrEmpty(w.Kana) ? null : w.Kana;

    /// <summary>Romanisation / transliteration (e.g. romaji).</summary>
    public virtual string TransliterationForm(Word w) => w.Romaji;

    /// <summary>Form used for phonetic similarity comparisons (Levenshtein on this string).</summary>
    public virtual string PhoneticForm(Word w) => w.Kana;

    /// <summary>Text handed to the TTS engine.</summary>
    public virtual string TtsText(Word w) => w.Kana;

    /// <summary>
    /// Switch-case factory — no reflection. Adding a behaviour means adding an enum value
    /// here AND a constructor that accepts <paramref name="config"/> (or ignores it).
    /// </summary>
    public static LanguageBehavior Create(LanguageBehaviorType type, JsonElement? config) =>
        type switch
        {
            LanguageBehaviorType.Japanese => new JapaneseBehavior(config),
            _                             => new GenericBehavior(config)
        };
}

/// <summary>Default behaviour: pure data, no language-specific logic. Used when a pack
/// doesn't declare a type, or when the configured type isn't recognised.</summary>
public sealed class GenericBehavior : LanguageBehavior
{
    public GenericBehavior(JsonElement? config) { /* no config consumed yet */ }
}

/// <summary>
/// Japanese-specific overrides. Tightens glyph detection to also match the id-prefix
/// convention used in the bundled vocab (h-* hiragana, k-* katakana).
/// </summary>
public sealed class JapaneseBehavior : LanguageBehavior
{
    public JapaneseBehavior(JsonElement? config) { /* no config consumed yet */ }

    public override bool IsGlyphEntry(Word w, IReadOnlyList<string> glyphTags)
    {
        if (w.Id.StartsWith("h-", StringComparison.Ordinal)) return true;
        if (w.Id.StartsWith("k-", StringComparison.Ordinal)) return true;
        return base.IsGlyphEntry(w, glyphTags);
    }
}
