using System.Collections.ObjectModel;
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

    public TagFilterViewModel(IVocabularyService vocab, ISettingsService settings)
    {
        _vocab = vocab;
        _settings = settings;
    }

    public async Task RefreshAsync()
    {
        await _vocab.EnsureLoadedAsync();

        // Build a map: tag -> count, sorted by count desc then alphabetic.
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var w in _vocab.All)
        {
            foreach (var t in w.Tags)
            {
                if (string.IsNullOrWhiteSpace(t)) continue;
                counts[t] = counts.TryGetValue(t, out var c) ? c + 1 : 1;
            }
        }

        var ordered = counts
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
        foreach (var (tag, count) in ordered)
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
