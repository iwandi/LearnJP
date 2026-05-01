namespace LearnJP.Services;

public interface ISettingsService
{
    bool RomajiOnly { get; set; }
    bool TtsEnabled { get; set; }
    bool ForceFurigana { get; set; }
    double TtsRate { get; set; }
}
