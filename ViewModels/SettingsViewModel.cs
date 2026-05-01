using LearnJP.Services;

namespace LearnJP.ViewModels;

public sealed class SettingsViewModel : BaseViewModel
{
    private readonly ISettingsService _settings;

    public SettingsViewModel(ISettingsService settings) { _settings = settings; }

    public bool RomajiOnly
    {
        get => _settings.RomajiOnly;
        set { _settings.RomajiOnly = value; OnPropertyChanged(); }
    }

    public bool TtsEnabled
    {
        get => _settings.TtsEnabled;
        set { _settings.TtsEnabled = value; OnPropertyChanged(); }
    }

    public bool ForceFurigana
    {
        get => _settings.ForceFurigana;
        set { _settings.ForceFurigana = value; OnPropertyChanged(); }
    }

    public double TtsRate
    {
        get => _settings.TtsRate;
        set { _settings.TtsRate = value; OnPropertyChanged(); }
    }
}
