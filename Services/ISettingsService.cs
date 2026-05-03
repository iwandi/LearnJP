using LearnJP.Models;

namespace LearnJP.Services;

public interface ISettingsService
{
    bool RomajiOnly { get; set; }
    bool TtsEnabled { get; set; }
    bool ForceFurigana { get; set; }
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
}
