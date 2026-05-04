using System.Collections.ObjectModel;
using LearnJP.Models;
using LearnJP.Services;

namespace LearnJP.ViewModels;

/// <summary>One toggle in the active language's display-option list. The settings layer
/// doesn't know what the key means — only the active <see cref="LanguageBehavior"/> does —
/// so this VM is just a generic "named bool, persisted per pack" wrapper.</summary>
public sealed class DisplayFlagVm : BaseViewModel
{
    private readonly ISettingsService _settings;
    private readonly string _packId;
    private readonly bool _defaultValue;
    private bool _value;

    public string Key { get; }
    public string Display { get; }

    public DisplayFlagVm(ISettingsService settings, string packId, DisplayOption option)
    {
        _settings = settings;
        _packId = packId;
        Key = option.Key;
        Display = option.Display;
        _defaultValue = option.DefaultValue;
        _value = settings.GetDisplayFlag(packId, option.Key, option.DefaultValue);
    }

    public bool Value
    {
        get => _value;
        set
        {
            if (!SetProperty(ref _value, value)) return;
            _settings.SetDisplayFlag(_packId, Key, value);
        }
    }
}

/// <summary>Entry in the UI-language override picker. The empty code means auto-detect.</summary>
public sealed class UiLanguageOption
{
    public required string Code { get; init; }
    public required string Display { get; init; }
}

public sealed class SettingsViewModel : BaseViewModel
{
    private readonly ISettingsService _settings;
    private readonly ILanguagePackService _packs;
    private readonly ILocalizationService _loc;

    public ILocalizationService Loc => _loc;

    public SettingsViewModel(ISettingsService settings, ILanguagePackService packs, ILocalizationService loc)
    {
        _settings = settings;
        _packs = packs;
        _loc = loc;
        TtsProviders = new ObservableCollection<TtsProvider>(Enum.GetValues<TtsProvider>());
        // Rebuild the dynamic toggle list whenever the user picks a different language pack.
        _packs.ActiveChanged += (_, _) => RebuildDisplayFlags();
        RebuildDisplayFlags();
        BuildUiLanguageOptions();
    }

    public ObservableCollection<TtsProvider> TtsProviders { get; }

    /// <summary>Dynamic list of toggles sourced from the active pack's
    /// <see cref="LanguageBehavior.DisplayOptions"/>. Empty for languages that don't expose any.</summary>
    public ObservableCollection<DisplayFlagVm> DisplayFlags { get; } = new();

    public bool HasDisplayFlags => DisplayFlags.Count > 0;

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

    /// <summary>Called externally (e.g. from <c>SettingsPage.OnAppearing</c>) to make sure
    /// the toggle list reflects the active pack on first display, since the pack service may
    /// finish loading after this view-model was constructed.</summary>
    public void RebuildDisplayFlags()
    {
        DisplayFlags.Clear();
        var pack = _packs.Active;
        if (pack is null)
        {
            OnPropertyChanged(nameof(HasDisplayFlags));
            return;
        }
        foreach (var opt in pack.Behavior.DisplayOptions)
            DisplayFlags.Add(new DisplayFlagVm(_settings, pack.Id, opt));
        OnPropertyChanged(nameof(HasDisplayFlags));
    }

    // ── UI language override ──────────────────────────────────────────────────────

    public ObservableCollection<UiLanguageOption> UiLanguageOptions { get; } = new();

    private UiLanguageOption? _selectedUiLanguage;

    public UiLanguageOption? SelectedUiLanguage
    {
        get => _selectedUiLanguage;
        set
        {
            if (!SetProperty(ref _selectedUiLanguage, value) || value is null) return;
            _loc.ApplyOverride(string.IsNullOrEmpty(value.Code) ? null : value.Code);
        }
    }

    private void BuildUiLanguageOptions()
    {
        UiLanguageOptions.Clear();
        UiLanguageOptions.Add(new UiLanguageOption { Code = string.Empty, Display = _loc["settings_ui_language_auto"] });
        UiLanguageOptions.Add(new UiLanguageOption { Code = "en", Display = "English" });
        UiLanguageOptions.Add(new UiLanguageOption { Code = "de", Display = "Deutsch" });

        var current = _settings.UiLanguageOverride;
        _selectedUiLanguage = UiLanguageOptions.FirstOrDefault(
            o => string.Equals(o.Code, current, StringComparison.OrdinalIgnoreCase))
            ?? UiLanguageOptions[0];
    }
}
