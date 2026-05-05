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

/// <summary>Display row for one stage in the progression ladder.</summary>
public sealed class ProgressionStageRow : BaseViewModel
{
    public required string Tag { get; init; }
    public required int TotalWords { get; init; }
    public required int KnownWords { get; init; }
    public required double UnlockThreshold { get; init; }
    public required bool IsUnlocked { get; init; }

    public bool IsLocked => !IsUnlocked;
    public string StatusIcon  => IsUnlocked ? "🔓" : "🔒";
    public string StatusLabel => IsUnlocked ? "Unlocked" : "Locked";
    public string ProgressText => $"{KnownWords}/{TotalWords}";
    public double KnownFraction => TotalWords > 0 ? (double)KnownWords / TotalWords : 0;
    public string ThresholdText => IsUnlocked ? string.Empty : $"Need {UnlockThreshold:P0} of previous stage";
}

public sealed class TagFilterViewModel : BaseViewModel
{
    private readonly IVocabularyService _vocab;
    private readonly ISettingsService _settings;
    private readonly ILanguagePackService _packs;
    private readonly ILocalizationService _loc;
    private readonly IProgressionService _progression;
    private readonly IProficiencyStore _store;

    public ILocalizationService Loc => _loc;
    public ObservableCollection<TagOption> Tags { get; } = new();
    public ObservableCollection<ProgressionStageRow> ProgressionStages { get; } = new();

    /// <summary>Language-behavior display flags (e.g. Include kana, Romaji-only mode).
    /// Mirrors the same logic as SettingsViewModel but hosted here so users find all
    /// language-specific customisation in one place.</summary>
    public ObservableCollection<DisplayFlagVm> DisplayFlags { get; } = new();
    public bool HasDisplayFlags => DisplayFlags.Count > 0;

    // ── Filter mode ────────────────────────────────────────────────────────────
    public TagFilterMode FilterMode
    {
        get => _settings.TagFilterMode;
        set
        {
            if (_settings.TagFilterMode == value) return;
            _settings.TagFilterMode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsAutoProgression));
            OnPropertyChanged(nameof(IsManualFilter));
            OnPropertyChanged(nameof(ActiveFilterDisplay));
            OnPropertyChanged(nameof(AutoModeButtonColor));
            OnPropertyChanged(nameof(ManualButtonColor));
            OnPropertyChanged(nameof(AutoModeButtonTextColor));
            OnPropertyChanged(nameof(ManualButtonTextColor));
        }
    }

    public bool IsAutoProgression => FilterMode == TagFilterMode.AutoProgression;
    // NoFilter is treated as Manual in the UI (legacy packs that stored NoFilter keep
    // the no-tags-selected Manual behaviour; new users only see Auto / Manual).
    public bool IsManualFilter    => FilterMode == TagFilterMode.Manual || FilterMode == TagFilterMode.NoFilter;

    // Mode button colours — active mode uses accent colour.
    private Color ActiveButtonBg   => Color.FromArgb("#5B6BF5");
    private Color InactiveButtonBg => Application.Current?.RequestedTheme == AppTheme.Dark
        ? Color.FromArgb("#2A2A40") : Color.FromArgb("#E2E4F0");
    private Color ActiveButtonFg   => Colors.White;
    private Color InactiveButtonFg => Application.Current?.RequestedTheme == AppTheme.Dark
        ? Colors.White : Color.FromArgb("#1A1A2E");

    public Color AutoModeButtonColor     => IsAutoProgression ? ActiveButtonBg   : InactiveButtonBg;
    public Color AutoModeButtonTextColor => IsAutoProgression ? ActiveButtonFg   : InactiveButtonFg;
    public Color ManualButtonColor       => IsManualFilter    ? ActiveButtonBg   : InactiveButtonBg;
    public Color ManualButtonTextColor   => IsManualFilter    ? ActiveButtonFg   : InactiveButtonFg;

    // ── Active filter summary ───────────────────────────────────────────────────
    public string ActiveFilterDisplay
    {
        get
        {
            if (IsAutoProgression)
            {
                var tags = _progression.GetUnlockedTags();
                return tags.Count == 0
                    ? _loc["filter_active_no_filter"]
                    : _loc["filter_active_auto_prefix"] + string.Join(", ", tags);
            }
            // Manual / NoFilter
            var inc = _settings.ActiveIncludeTags;
            var exc = _settings.ActiveExcludeTags;
            if (inc.Count == 0 && exc.Count == 0) return _loc["filter_active_no_filter"];
            var parts = new List<string>();
            if (inc.Count > 0) parts.Add("+ " + string.Join(", ", inc));
            if (exc.Count > 0) parts.Add("− " + string.Join(", ", exc));
            return _loc["filter_active_prefix"] + string.Join("   ", parts);
        }
    }

    // ── Strategy picker ─────────────────────────────────────────────────────────
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

    public TagFilterViewModel(IVocabularyService vocab, ISettingsService settings, ILanguagePackService packs, ILocalizationService loc, IProgressionService progression, IProficiencyStore store)
    {
        _vocab = vocab;
        _settings = settings;
        _packs = packs;
        _loc = loc;
        _progression = progression;
        _store = store;

        // Rebuild language display flags whenever the active pack changes.
        _packs.ActiveChanged += (_, _) => RebuildDisplayFlags();
    }

    // ── Mode selection helpers called from code-behind ──────────────────────────
    public void SelectAutoProgression() => FilterMode = TagFilterMode.AutoProgression;
    public void SelectManual()          => FilterMode = TagFilterMode.Manual;

    // ── Language display flags ───────────────────────────────────────────────────
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

    // ── Manual tag include/exclude ───────────────────────────────────────────────
    public void ToggleInclude(TagOption opt)
    {
        if (opt.IsNoFilter) { ClearAll(); return; }
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

    // ── Refresh ──────────────────────────────────────────────────────────────────
    public async Task RefreshAsync()
    {
        await _vocab.EnsureLoadedAsync();
        await _store.LoadAsync();

        RebuildDisplayFlags();

        // ── Rebuild progression ladder ──────────────────────────────────────────
        var pack = _packs.Active;
        ProgressionStages.Clear();
        if (pack is not null && pack.Progression.Count > 0)
        {
            // Count total and known words per tag.
            var totalByTag = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var knownByTag = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var w in _vocab.All)
            {
                foreach (var tag in w.Tags)
                {
                    if (string.IsNullOrEmpty(tag)) continue;
                    totalByTag[tag] = totalByTag.TryGetValue(tag, out var t) ? t + 1 : 1;
                }
            }
            // GetUnlockedTags already queries the store; replicate known-count logic here for display.
            var unlockedSet = new HashSet<string>(_progression.GetUnlockedTags(), StringComparer.OrdinalIgnoreCase);

            foreach (var w in _vocab.All)
            {
                // Accessing proficiency synchronously is fine — store is already loaded.
                bool isKnown = IsWordKnown(w.Id);
                foreach (var tag in w.Tags)
                {
                    if (string.IsNullOrEmpty(tag)) continue;
                    if (isKnown)
                        knownByTag[tag] = knownByTag.TryGetValue(tag, out var k) ? k + 1 : 1;
                }
            }

            for (int i = 0; i < pack.Progression.Count; i++)
            {
                var stage = pack.Progression[i];
                if (string.IsNullOrEmpty(stage.Tag)) continue;
                totalByTag.TryGetValue(stage.Tag, out var total);
                knownByTag.TryGetValue(stage.Tag, out var known);
                ProgressionStages.Add(new ProgressionStageRow
                {
                    Tag = stage.Tag,
                    TotalWords = total,
                    KnownWords = known,
                    UnlockThreshold = i == 0 ? 0.0 : stage.UnlockThreshold,
                    IsUnlocked = unlockedSet.Contains(stage.Tag),
                });
            }
        }

        // ── Rebuild manual tag list ─────────────────────────────────────────────
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var w in _vocab.All)
        {
            foreach (var t in w.Tags)
            {
                if (string.IsNullOrWhiteSpace(t)) continue;
                counts[t] = counts.TryGetValue(t, out var c) ? c + 1 : 1;
            }
        }

        var pinned = _packs.Active?.GlyphTags ?? new List<string>();
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

    // Reads IsKnown from the store's in-memory cache synchronously (store.LoadAsync already
    // called by RefreshAsync before this is used).
    private bool IsWordKnown(string wordId) => _store.Get(wordId).IsKnown;
}
