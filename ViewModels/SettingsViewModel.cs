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

    public bool CountForProficiency
    {
        get => _settings.CountForProficiency;
        set { _settings.CountForProficiency = value; OnPropertyChanged(); }
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
            OnPropertyChanged(nameof(IsSystemSelected));
        }
    }

    public bool IsAzureSelected => SelectedTtsProvider == TtsProvider.Azure;
    public bool IsSystemSelected => SelectedTtsProvider == TtsProvider.System;

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

    public double SystemTtsVolume
    {
        get => _settings.SystemTtsVolume;
        set
        {
            _settings.SystemTtsVolume = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SystemTtsVolumeText));
        }
    }

    public string SystemTtsVolumeText => $"{(int)Math.Round(SystemTtsVolume * 100)}%";

    public double AzureTtsVolume
    {
        get => _settings.AzureTtsVolume;
        set
        {
            _settings.AzureTtsVolume = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(AzureTtsVolumeText));
        }
    }

    public string AzureTtsVolumeText => $"{(int)Math.Round(AzureTtsVolume * 100)}%";
}
