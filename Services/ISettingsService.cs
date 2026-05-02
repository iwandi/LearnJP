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
}
