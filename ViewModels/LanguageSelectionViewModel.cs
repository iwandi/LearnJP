using System.Collections.ObjectModel;
using LearnJP.Models;
using LearnJP.Services;

namespace LearnJP.ViewModels;

public sealed class LanguageOption : BaseViewModel
{
    private bool _isActive;

    public required LanguagePack Pack { get; init; }
    public string Id => Pack.Id;
    public string DisplayName => Pack.DisplayName;
    public string Subtitle => Pack.TtsLocale;

    public bool IsActive
    {
        get => _isActive;
        set { if (SetProperty(ref _isActive, value)) { OnPropertyChanged(nameof(BackgroundColor)); OnPropertyChanged(nameof(SelectButtonText)); } }
    }

    public Color BackgroundColor => _isActive
        ? Color.FromArgb("#4CAF7A")
        : (Application.Current?.RequestedTheme == AppTheme.Dark ? Color.FromArgb("#1B1B2E") : Color.FromArgb("#F7F8FF"));

    public string SelectButtonText => _isActive ? "Active" : "Select";
}

public sealed class BaseLanguageOption
{
    public required string Code { get; init; }
    public required string Display { get; init; }
}

public sealed class LanguageSelectionViewModel : BaseViewModel
{
    private readonly ILanguagePackService _packs;
    private readonly ISettingsService _settings;
    private string _activeName = string.Empty;
    private BaseLanguageOption? _selectedBase;
    private bool _suppressBaseChange;

    public ObservableCollection<LanguageOption> Languages { get; } = new();
    public ObservableCollection<BaseLanguageOption> BaseLanguages { get; } = new();

    public string ActiveName { get => _activeName; private set => SetProperty(ref _activeName, value); }

    /// <summary>Two-way binding target for the base-language Picker.</summary>
    public BaseLanguageOption? SelectedBase
    {
        get => _selectedBase;
        set
        {
            if (!SetProperty(ref _selectedBase, value)) return;
            if (_suppressBaseChange || value is null) return;
            _settings.BaseLanguageId = value.Code;
            // Re-filter the target list because available targets may depend on which bases
            // they translate into.
            RefreshTargetList();
        }
    }

    public LanguageSelectionViewModel(ILanguagePackService packs, ISettingsService settings)
    {
        _packs = packs;
        _settings = settings;
    }

    public async Task RefreshAsync()
    {
        await _packs.EnsureLoadedAsync();

        // Build the base-language list from every translation key declared by every pack,
        // de-duplicated. Always include the current setting even if no pack supports it
        // (so the user's persisted choice doesn't silently change on launch).
        var baseCodes = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in _packs.All)
            foreach (var key in p.Translations.Keys)
                baseCodes.Add(key);
        var current = _settings.BaseLanguageId;
        if (!string.IsNullOrWhiteSpace(current)) baseCodes.Add(current);
        if (baseCodes.Count == 0) baseCodes.Add("en");

        BaseLanguages.Clear();
        foreach (var code in baseCodes)
            BaseLanguages.Add(new BaseLanguageOption { Code = code, Display = LanguageDisplay(code) });

        // Set the picker's selection without re-triggering the SelectedBase setter side-effects.
        _suppressBaseChange = true;
        SelectedBase = BaseLanguages.FirstOrDefault(b => b.Code.Equals(current, StringComparison.OrdinalIgnoreCase))
                     ?? BaseLanguages.First();
        _suppressBaseChange = false;

        RefreshTargetList();
    }

    private void RefreshTargetList()
    {
        var activeId = _packs.Active?.Id ?? string.Empty;
        var baseCode = _selectedBase?.Code ?? "en";

        Languages.Clear();
        foreach (var p in _packs.All)
        {
            // Hide target packs that don't translate into the chosen base — picking one would
            // give the user a vocabulary list with no meanings.
            if (p.Translations.Count > 0 && !p.Translations.ContainsKey(baseCode)) continue;
            Languages.Add(new LanguageOption
            {
                Pack = p,
                IsActive = string.Equals(p.Id, activeId, StringComparison.OrdinalIgnoreCase)
            });
        }
        ActiveName = _packs.Active?.DisplayName ?? "(none)";
    }

    public async Task SelectAsync(LanguageOption option)
    {
        if (option.IsActive) return;
        await _packs.SetActiveAsync(option.Id);
        foreach (var lang in Languages) lang.IsActive = string.Equals(lang.Id, option.Id, StringComparison.OrdinalIgnoreCase);
        ActiveName = option.DisplayName;
    }

    /// <summary>Human-readable name for a base-language code; falls back to the code itself.</summary>
    private static string LanguageDisplay(string code) => code.ToLowerInvariant() switch
    {
        "en" => "English",
        "de" => "German",
        "fr" => "French",
        "es" => "Spanish",
        "it" => "Italian",
        "pt" => "Portuguese",
        "nl" => "Dutch",
        "ru" => "Russian",
        "zh" => "Chinese",
        "ja" => "Japanese",
        "ko" => "Korean",
        _    => code
    };
}
