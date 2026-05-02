using System.Collections.ObjectModel;
using LearnJP.Models;
using LearnJP.Services;

namespace LearnJP.ViewModels;

public sealed class SettingsViewModel : BaseViewModel
{
    private readonly ISettingsService _settings;

    public SettingsViewModel(ISettingsService settings)
    {
        _settings = settings;
        TtsProviders = new ObservableCollection<TtsProvider>(Enum.GetValues<TtsProvider>());
    }

    public ObservableCollection<TtsProvider> TtsProviders { get; }

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

    public TtsProvider SelectedTtsProvider
    {
        get => _settings.TtsProvider;
        set
        {
            _settings.TtsProvider = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsAzureSelected));
        }
    }

    public bool IsAzureSelected => SelectedTtsProvider == TtsProvider.Azure;

    public string AzureSpeechKey
    {
        get => _settings.AzureSpeechKey;
        set { _settings.AzureSpeechKey = value; OnPropertyChanged(); }
    }

    public string AzureSpeechRegion
    {
        get => _settings.AzureSpeechRegion;
        set { _settings.AzureSpeechRegion = value; OnPropertyChanged(); }
    }

    public string AzureJapaneseVoice
    {
        get => _settings.AzureJapaneseVoice;
        set { _settings.AzureJapaneseVoice = value; OnPropertyChanged(); }
    }

    public string AzureEnglishVoice
    {
        get => _settings.AzureEnglishVoice;
        set { _settings.AzureEnglishVoice = value; OnPropertyChanged(); }
    }
}
