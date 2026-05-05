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
/// Per-language hooks the algorithm calls into. Behaviours read <see cref="Word.Forms"/>
/// through <see cref="FormOf"/> (which translates role names into the pack's positional
/// indices) so that callers never bind to specific writing systems. Defaults below assume
/// "this language has one form" — slot 0 — which is correct for the bulk of languages.
/// JP-style multi-script languages override the role accessors and the prompt renderer.
/// </summary>
public abstract class LanguageBehavior
{
    /// <summary>Back-reference to the owning pack. Populated by the pack loader after the
    /// behaviour is constructed; behaviours that need the pack at construction time must
    /// defer that work until the first virtual call. Null only during the brief window
    /// inside <see cref="Create"/> before the loader assigns it.</summary>
    public LanguagePack? Pack { get; internal set; }

    /// <summary>Resolves a named form (e.g. "kana") to a value on <paramref name="w"/>.
    /// Returns empty when the pack doesn't declare that role or the word didn't fill it.</summary>
    protected string FormOf(Word w, string formKey)
    {
        var idx = Pack?.IndexOfForm(formKey) ?? -1;
        return w.FormAt(idx);
    }

    /// <summary>
    /// Well-known display-flag key that controls whether words tagged with the pack's
    /// <see cref="LanguagePack.GlyphTags"/> are included in the quiz pool. Behaviours that
    /// have a glyph sub-system (e.g. Japanese) declare this in their
    /// <see cref="DisplayOptions"/> with <c>defaultValue: false</c>. The question generator
    /// reads it so the toggle wires directly to pool filtering without any extra glue code.
    /// </summary>
    public const string FlagIncludeGlyphs = "includeGlyphs";

    /// <summary>True when the entry represents a single character / atomic glyph (e.g. one
    /// hiragana). These are excluded from general practice unless explicitly opted in.</summary>
    public virtual bool IsGlyphEntry(Word w, IReadOnlyList<string> glyphTags) =>
        glyphTags.Any(t => w.Tags.Contains(t, StringComparer.OrdinalIgnoreCase));

    /// <summary>Primary written form to display as the prompt. Default: slot 0.</summary>
    public virtual string PrimaryForm(Word w) => w.FormAt(0);

    /// <summary>Reading aid (e.g. kana / furigana). Null when the language has no separate
    /// reading line.</summary>
    public virtual string? ReadingForm(Word w) => null;

    /// <summary>Romanisation / transliteration. Default: same as the primary form.</summary>
    public virtual string TransliterationForm(Word w) => PrimaryForm(w);

    /// <summary>Form used for phonetic similarity comparisons. Default: primary form.</summary>
    public virtual string PhoneticForm(Word w) => PrimaryForm(w);

    /// <summary>Text handed to the TTS engine. Default: primary form.</summary>
    public virtual string TtsText(Word w) => PrimaryForm(w);

    /// <summary>Display options the language exposes to the UI. The settings layer persists
    /// these by key (per active pack) without knowing what they mean — only the behaviour
    /// reads them back inside <see cref="RenderPrompt"/> / <see cref="OptionDisplay"/>.</summary>
    public virtual IReadOnlyList<DisplayOption> DisplayOptions => Array.Empty<DisplayOption>();

    /// <summary>
    /// Renders the prompt for a question. Behaviours decide which form to show given the
    /// question direction, the user's current proficiency on the target, and the active
    /// display flags. The default uses <see cref="PrimaryForm"/> with no furigana.
    /// </summary>
    public virtual PromptRender RenderPrompt(Word target, QuestionDirection dir, double overall, IDisplayFlags flags)
    {
        if (dir == QuestionDirection.BaseToTarget)
            return new PromptRender(target.MeaningsJoined, null);
        return new PromptRender(PrimaryForm(target), null);
    }

    /// <summary>Display label used inside the option list when options are in the target
    /// language (i.e. base→target direction). Default: just the primary form.</summary>
    public virtual string OptionDisplay(Word w, IDisplayFlags flags) => PrimaryForm(w);

    /// <summary>Display label used after the user has answered, regardless of direction —
    /// shows the target-language form alongside the meaning so the user can verify every
    /// option at a glance. Default: "&lt;primary&gt; — &lt;meaning&gt;".</summary>
    public virtual string RevealedDisplay(Word w, IDisplayFlags flags) =>
        $"{PrimaryForm(w)} — {w.PrimaryMeaning}";

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
/// doesn't declare a type, or when the configured type isn't recognised. All accessors
/// return <see cref="LanguageBehavior.PrimaryForm"/>, which is just <c>Forms[0]</c>.</summary>
public sealed class GenericBehavior : LanguageBehavior
{
    public GenericBehavior(JsonElement? config) { /* no config consumed yet */ }
}

/// <summary>
/// Japanese-specific overrides. Reads kanji / kana / romaji forms by name (the JP pack
/// declares a 3-slot <c>forms</c> list). Owns its two display flags — "romaji-only" and
/// "always show furigana" — plus the proficiency-driven prompt renderer that gradually
/// shifts the user from hiragana to kanji-with-furigana to kanji-only.
/// </summary>
public sealed class JapaneseBehavior : LanguageBehavior
{
    public const string FlagRomajiOnly    = "romajiOnly";
    public const string FlagForceFurigana = "forceFurigana";

    public JapaneseBehavior(JsonElement? config) { /* no config consumed yet */ }

    public override string PrimaryForm(Word w)
    {
        var k = FormOf(w, "kanji");
        return string.IsNullOrEmpty(k) ? FormOf(w, "kana") : k;
    }

    public override string? ReadingForm(Word w)
    {
        var s = FormOf(w, "kana");
        return string.IsNullOrEmpty(s) ? null : s;
    }

    public override string TransliterationForm(Word w) => FormOf(w, "romaji");
    public override string PhoneticForm(Word w) => FormOf(w, "kana");
    // Use kana for TTS to guarantee unambiguous pronunciation — bundled assets are
    // looked up by word ID, so the text value does not affect bundled-asset resolution.
    public override string TtsText(Word w)
    {
        var kana = FormOf(w, "kana");
        return string.IsNullOrEmpty(kana) ? PrimaryForm(w) : kana;
    }

    public override IReadOnlyList<DisplayOption> DisplayOptions => _options;
    private static readonly DisplayOption[] _options =
    {
        new(FlagIncludeGlyphs,  "Include hiragana/katakana", false),
        new(FlagRomajiOnly,    "Romaji-only mode"),
        new(FlagForceFurigana, "Always show furigana"),
    };

    public override bool IsGlyphEntry(Word w, IReadOnlyList<string> glyphTags)
    {
        if (w.Id.StartsWith("h-", StringComparison.Ordinal)) return true;
        if (w.Id.StartsWith("k-", StringComparison.Ordinal)) return true;
        return base.IsGlyphEntry(w, glyphTags);
    }

    public override PromptRender RenderPrompt(Word target, QuestionDirection dir, double overall, IDisplayFlags flags)
    {
        if (dir == QuestionDirection.BaseToTarget)
            return new PromptRender(target.MeaningsJoined, null);

        if (flags.Get(FlagRomajiOnly))
            return new PromptRender(FormOf(target, "romaji"), null);

        var kanji = FormOf(target, "kanji");
        var kana  = FormOf(target, "kana");

        // Same proficiency-banded display ladder as the previous JapaneseDisplayMode enum:
        // hiragana-only while the kanji surface is unfamiliar, kanji+furigana while learning,
        // kanji-only once mastered (or always on, if the user forces furigana).
        if (overall < 25)
            return new PromptRender(string.IsNullOrEmpty(kana) ? kanji : kana, null);

        if (overall < 70 || flags.Get(FlagForceFurigana))
        {
            var p = string.IsNullOrEmpty(kanji) ? kana : kanji;
            var f = string.IsNullOrEmpty(kanji) ? null : kana;
            return new PromptRender(p, f);
        }

        return new PromptRender(string.IsNullOrEmpty(kanji) ? kana : kanji, null);
    }

    public override string OptionDisplay(Word w, IDisplayFlags flags)
    {
        if (flags.Get(FlagRomajiOnly)) return FormOf(w, "romaji");
        var kanji = FormOf(w, "kanji");
        var kana  = FormOf(w, "kana");
        if (string.IsNullOrEmpty(kanji)) return kana;
        return $"{kanji}  ({kana})";
    }

    public override string RevealedDisplay(Word w, IDisplayFlags flags)
    {
        var kanji = FormOf(w, "kanji");
        var kana  = FormOf(w, "kana");
        var primary = string.IsNullOrEmpty(kanji) ? kana
                      : (kanji == kana ? kanji : $"{kanji} ({kana})");
        return $"{primary} — {w.PrimaryMeaning}";
    }
}
