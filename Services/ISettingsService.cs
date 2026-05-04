using LearnJP.Models;

namespace LearnJP.Services;

public interface ISettingsService
{
    bool TtsEnabled { get; set; }
    double TtsRate { get; set; }

    TtsProvider TtsProvider { get; set; }
    string AzureSpeechKey { get; set; }
    string AzureSpeechRegion { get; set; }

    /// <summary>0.0 .. 1.0. Applied at playback, not synthesis.</summary>
    double SystemTtsVolume { get; set; }
    /// <summary>0.0 .. 1.0. Applied at playback, not synthesis (so the cache stays voice-only).</summary>
    double AzureTtsVolume { get; set; }

    /// <summary>Tags that words must match (any-of) to enter the pool. Empty = no include constraint.</summary>
    IReadOnlyList<string> ActiveIncludeTags { get; set; }

    /// <summary>Tags that disqualify a word from the pool (any-of). Empty = no exclude constraint.</summary>
    IReadOnlyList<string> ActiveExcludeTags { get; set; }

    /// <summary>Persisted target-picking strategy.</summary>
    LearningStrategy SelectedLearningStrategy { get; set; }

    /// <summary>When false, answers are not recorded against per-word proficiency (debugging).</summary>
    bool CountForProficiency { get; set; }

    /// <summary>Id of the currently selected language pack (e.g. "ja"). Empty = first available.</summary>
    string ActiveLanguageId { get; set; }

    /// <summary>Base-language code the user wants meanings displayed in (e.g. "en", "de").</summary>
    string BaseLanguageId { get; set; }

    /// <summary>
    /// Reads a per-pack display flag. The settings layer never defines what these keys mean —
    /// the active <see cref="LanguageBehavior"/> declares them through
    /// <see cref="LanguageBehavior.DisplayOptions"/> and consumes them from this same
    /// generic store. Flags are scoped per pack so e.g. JP's "always show furigana" doesn't
    /// pollute Italian's settings.
    /// </summary>
    bool GetDisplayFlag(string packId, string key, bool defaultValue);

    /// <summary>Persists a per-pack display flag. See <see cref="GetDisplayFlag"/>.</summary>
    void SetDisplayFlag(string packId, string key, bool value);

    /// <summary>Returns a read-only view of all display flags for the given pack, suitable
    /// for passing to <see cref="LanguageBehavior"/>'s render methods.</summary>
    IDisplayFlags DisplayFlagsFor(string packId);

    /// <summary>
    /// ISO 639-1 language code chosen by the user to override UI language auto-detection
    /// (e.g. "en", "de"). Empty string means auto-detect from
    /// <see cref="System.Globalization.CultureInfo.CurrentUICulture"/>.
    /// </summary>
    string UiLanguageOverride { get; set; }
}
