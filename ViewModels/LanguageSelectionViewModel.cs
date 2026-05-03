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

public sealed class LanguageSelectionViewModel : BaseViewModel
{
    private readonly ILanguagePackService _packs;
    private string _activeName = string.Empty;

    public ObservableCollection<LanguageOption> Languages { get; } = new();

    public string ActiveName { get => _activeName; private set => SetProperty(ref _activeName, value); }

    public LanguageSelectionViewModel(ILanguagePackService packs) { _packs = packs; }

    public async Task RefreshAsync()
    {
        await _packs.EnsureLoadedAsync();
        var activeId = _packs.Active?.Id ?? string.Empty;
        Languages.Clear();
        foreach (var p in _packs.All)
            Languages.Add(new LanguageOption { Pack = p, IsActive = string.Equals(p.Id, activeId, StringComparison.OrdinalIgnoreCase) });
        ActiveName = _packs.Active?.DisplayName ?? "(none)";
    }

    public async Task SelectAsync(LanguageOption option)
    {
        if (option.IsActive) return;
        await _packs.SetActiveAsync(option.Id);
        foreach (var lang in Languages) lang.IsActive = string.Equals(lang.Id, option.Id, StringComparison.OrdinalIgnoreCase);
        ActiveName = option.DisplayName;
    }
}
