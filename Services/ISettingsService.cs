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
    string AzureJapaneseVoice { get; set; }
    string AzureEnglishVoice { get; set; }

    /// <summary>0.0 .. 1.0. Applied at playback, not synthesis.</summary>
    double SystemTtsVolume { get; set; }
    /// <summary>0.0 .. 1.0. Applied at playback, not synthesis (so the cache stays voice-only).</summary>
    double AzureTtsVolume { get; set; }
}
