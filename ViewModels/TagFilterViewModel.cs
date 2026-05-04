using System.Collections.ObjectModel;
using LearnJP.Models;
using LearnJP.Services;

namespace LearnJP.ViewModels;

public sealed class TagOption : BaseViewModel
{
    private bool _isIncluded;
    private bool _isExcluded;

    public required string Tag { get; init; }       // empty for "no filter"
    public required string Display { get; init; }
    public required int WordCount { get; init; }
    public required bool IsNoFilter { get; init; }
    public bool IsNotNoFilter => !IsNoFilter;

    public bool IsIncluded
    {
        get => _isIncluded;
        set { if (SetProperty(ref _isIncluded, value)) { OnPropertyChanged(nameof(IncludeButtonColor)); OnPropertyChanged(nameof(IncludeButtonTextColor)); } }
    }

    public bool IsExcluded
    {
        get => _isExcluded;
        set { if (SetProperty(ref _isExcluded, value)) { OnPropertyChanged(nameof(ExcludeButtonColor)); OnPropertyChanged(nameof(ExcludeButtonTextColor)); } }
    }

    public Color IncludeButtonColor => _isIncluded
        ? Color.FromArgb("#4CAF7A")
        : (Application.Current?.RequestedTheme == AppTheme.Dark ? Color.FromArgb("#2A2A40") : Color.FromArgb("#E2E4F0"));

    public Color IncludeButtonTextColor => _isIncluded
        ? Colors.White
        : (Application.Current?.RequestedTheme == AppTheme.Dark ? Colors.White : Color.FromArgb("#1A1A2E"));

    public Color ExcludeButtonColor => _isExcluded
        ? Color.FromArgb("#E5556B")
        : (Application.Current?.RequestedTheme == AppTheme.Dark ? Color.FromArgb("#2A2A40") : Color.FromArgb("#E2E4F0"));

    public Color ExcludeButtonTextColor => _isExcluded
        ? Colors.White
        : (Application.Current?.RequestedTheme == AppTheme.Dark ? Colors.White : Color.FromArgb("#1A1A2E"));
}

public sealed class TagFilterViewModel : BaseViewModel
{
    private readonly IVocabularyService _vocab;
    private readonly ISettingsService _settings;
    private readonly ILanguagePackService _packs;
    private readonly ILocalizationService _loc;

    public ILocalizationService Loc => _loc;
    public ObservableCollection<TagOption> Tags { get; } = new();

    public string ActiveFilterDisplay
    {
        get
        {
            var inc = _settings.ActiveIncludeTags;
            var exc = _settings.ActiveExcludeTags;
            if (inc.Count == 0 && exc.Count == 0) return _loc["filter_active_no_filter"];
            var parts = new List<string>();
            if (inc.Count > 0) parts.Add("+ " + string.Join(", ", inc));
            if (exc.Count > 0) parts.Add("− " + string.Join(", ", exc));
            return _loc["filter_active_prefix"] + string.Join("   ", parts);
        }
    }

    public ObservableCollection<LearningStrategy> Strategies { get; } =
        new(Enum.GetValues<LearningStrategy>());

    public LearningStrategy SelectedStrategy
    {
        get => _settings.SelectedLearningStrategy;
        set
        {
            if (_settings.SelectedLearningStrategy == value) return;
            _settings.SelectedLearningStrategy = value;
            OnPropertyChanged();
        }
    }

    public TagFilterViewModel(IVocabularyService vocab, ISettingsService settings, ILanguagePackService packs, ILocalizationService loc)
    {
        _vocab = vocab;
        _settings = settings;
        _packs = packs;
        _loc = loc;
    }

    public void ToggleInclude(TagOption opt)
    {
        if (opt.IsNoFilter) { ClearAll(); return; }
        // Include and exclude are mutually exclusive: turning one on disables the other.
        opt.IsIncluded = !opt.IsIncluded;
        if (opt.IsIncluded) opt.IsExcluded = false;
        Persist();
    }

    public void ToggleExclude(TagOption opt)
    {
        if (opt.IsNoFilter) { ClearAll(); return; }
        opt.IsExcluded = !opt.IsExcluded;
        if (opt.IsExcluded) opt.IsIncluded = false;
        Persist();
    }

    public void ClearAll()
    {
        foreach (var t in Tags) { t.IsIncluded = false; t.IsExcluded = false; }
        _settings.ActiveIncludeTags = Array.Empty<string>();
        _settings.ActiveExcludeTags = Array.Empty<string>();
        OnPropertyChanged(nameof(ActiveFilterDisplay));
    }

    private void Persist()
    {
        _settings.ActiveIncludeTags = Tags.Where(t => t.IsIncluded).Select(t => t.Tag).ToList();
        _settings.ActiveExcludeTags = Tags.Where(t => t.IsExcluded).Select(t => t.Tag).ToList();
        OnPropertyChanged(nameof(ActiveFilterDisplay));
    }

    public async Task RefreshAsync()
    {
        await _vocab.EnsureLoadedAsync();

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var w in _vocab.All)
        {
            foreach (var t in w.Tags)
            {
                if (string.IsNullOrWhiteSpace(t)) continue;
                counts[t] = counts.TryGetValue(t, out var c) ? c + 1 : 1;
            }
        }

        // Tags pinned to the top of the list (after "no filter") regardless of count.
        var pinned = _packs.Active?.GlyphTags;
        var rest = counts
            .Where(kv => !pinned.Contains(kv.Key, StringComparer.OrdinalIgnoreCase))
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .ToList();

        var include = new HashSet<string>(_settings.ActiveIncludeTags, StringComparer.OrdinalIgnoreCase);
        var exclude = new HashSet<string>(_settings.ActiveExcludeTags, StringComparer.OrdinalIgnoreCase);

        Tags.Clear();
        Tags.Add(new TagOption
        {
            Tag = string.Empty,
            Display = _loc["filter_no_filter"],
            WordCount = _vocab.All.Count,
            IsNoFilter = true
        });
        foreach (var p in pinned)
        {
            if (!counts.TryGetValue(p, out var c)) continue;
            Tags.Add(new TagOption
            {
                Tag = p,
                Display = $"{p}  ·  {c}",
                WordCount = c,
                IsNoFilter = false,
                IsIncluded = include.Contains(p),
                IsExcluded = exclude.Contains(p)
            });
        }
        foreach (var (tag, count) in rest)
        {
            Tags.Add(new TagOption
            {
                Tag = tag,
                Display = $"{tag}  ·  {count}",
                WordCount = count,
                IsNoFilter = false,
                IsIncluded = include.Contains(tag),
                IsExcluded = exclude.Contains(tag)
            });
        }

        OnPropertyChanged(nameof(ActiveFilterDisplay));
    }
}
