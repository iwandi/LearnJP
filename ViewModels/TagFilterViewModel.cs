using System.Collections.ObjectModel;
using LearnJP.Models;
using LearnJP.Services;

namespace LearnJP.ViewModels;

public sealed class TagOption
{
    public required string Tag { get; init; }       // empty for "no filter"
    public required string Display { get; init; }
    public required int WordCount { get; init; }
    public required bool IsNoFilter { get; init; }
}

public sealed class TagFilterViewModel : BaseViewModel
{
    private readonly IVocabularyService _vocab;
    private readonly ISettingsService _settings;
    private TagOption? _selected;

    public ObservableCollection<TagOption> Tags { get; } = new();

    public TagOption? SelectedTag
    {
        get => _selected;
        set
        {
            if (SetProperty(ref _selected, value) && value is not null)
            {
                _settings.ActiveTagFilter = value.IsNoFilter ? string.Empty : value.Tag;
                OnPropertyChanged(nameof(ActiveFilterDisplay));
            }
        }
    }

    public string ActiveFilterDisplay =>
        string.IsNullOrEmpty(_settings.ActiveTagFilter)
            ? "No filter — drawing from the full vocabulary."
            : $"Filter: {_settings.ActiveTagFilter}";

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

    public TagFilterViewModel(IVocabularyService vocab, ISettingsService settings)
    {
        _vocab = vocab;
        _settings = settings;
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
        var pinned = new[] { "hiragana", "katakana" };
        var rest = counts
            .Where(kv => !pinned.Contains(kv.Key, StringComparer.OrdinalIgnoreCase))
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .ToList();

        Tags.Clear();
        Tags.Add(new TagOption
        {
            Tag = string.Empty,
            Display = "(no filter — full vocabulary)",
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
                IsNoFilter = false
            });
        }
        foreach (var (tag, count) in rest)
        {
            Tags.Add(new TagOption
            {
                Tag = tag,
                Display = $"{tag}  ·  {count}",
                WordCount = count,
                IsNoFilter = false
            });
        }

        // Reflect persisted selection back into the picker.
        var current = _settings.ActiveTagFilter ?? string.Empty;
        _selected = Tags.FirstOrDefault(o => o.IsNoFilter
            ? string.IsNullOrEmpty(current)
            : string.Equals(o.Tag, current, StringComparison.OrdinalIgnoreCase));
        OnPropertyChanged(nameof(SelectedTag));
        OnPropertyChanged(nameof(ActiveFilterDisplay));
    }
}
