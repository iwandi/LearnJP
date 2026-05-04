using System.Collections.ObjectModel;
using System.Windows.Input;
using LearnJP.Models;
using LearnJP.Services;

namespace LearnJP.ViewModels;

public sealed class WordProgressRow : BaseViewModel
{
    public required string WordId   { get; init; }
    public required string TtsText  { get; init; }
    public required string Display  { get; init; }
    public required string Reading  { get; init; }
    public required string Meaning  { get; init; }

    private double _overall;
    private string _overallText = "";
    private Color  _overallColor = Colors.Gray;

    public double Overall
    {
        get => _overall;
        set => SetProperty(ref _overall, value);
    }
    public string OverallText
    {
        get => _overallText;
        set => SetProperty(ref _overallText, value);
    }
    public Color OverallColor
    {
        get => _overallColor;
        set => SetProperty(ref _overallColor, value);
    }

    // Commands are set by ProgressViewModel after construction.
    public ICommand? SpeakCommand     { get; set; }
    public ICommand? AdjustUpCommand  { get; set; }
    public ICommand? AdjustDownCommand{ get; set; }
}

public sealed class ProgressViewModel : BaseViewModel
{
    private readonly IVocabularyService _vocab;
    private readonly IProficiencyStore  _store;
    private readonly ILanguagePackService _packs;
    private readonly ILocalizationService _loc;
    private readonly ITtsService _tts;

    private string _summary     = "";
    private int    _total;
    private int    _seen;
    private int    _known;
    private int    _mastered;
    private string _searchText  = "";

    // Backing full list (sorted by proficiency descending); FilteredRows is the
    // subset shown in the CollectionView after applying the search filter.
    private readonly List<WordProgressRow> _allRows = new();

    public ILocalizationService Loc => _loc;

    public ObservableCollection<WordProgressRow> FilteredRows { get; } = new();

    public string Summary  { get => _summary;  private set => SetProperty(ref _summary,  value); }
    public int    Total    { get => _total;     private set => SetProperty(ref _total,    value); }
    public int    Seen     { get => _seen;      private set => SetProperty(ref _seen,     value); }
    public int    Known    { get => _known;     private set => SetProperty(ref _known,    value); }
    public int    Mastered { get => _mastered;  private set => SetProperty(ref _mastered, value); }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
                ApplyFilter();
        }
    }

    // Chart data: proficiency values in the same order as _allRows (desc).
    private IReadOnlyList<double> _chartValues = Array.Empty<double>();
    public IReadOnlyList<double> ChartValues
    {
        get => _chartValues;
        private set => SetProperty(ref _chartValues, value);
    }

    public ICommand ResetCommand { get; }

    public ProgressViewModel(
        IVocabularyService vocab,
        IProficiencyStore store,
        ILanguagePackService packs,
        ILocalizationService loc,
        ITtsService tts)
    {
        _vocab = vocab;
        _store = store;
        _packs = packs;
        _loc   = loc;
        _tts   = tts;

        ResetCommand = new Command(async () =>
        {
            bool confirmed = await Shell.Current.DisplayAlert(
                _loc["progress_reset_confirm_title"],
                _loc["progress_reset_confirm_message"],
                _loc["progress_reset_confirm_yes"],
                _loc["progress_reset_confirm_cancel"]);
            if (!confirmed) return;
            await _store.ResetAsync();
            await RefreshAsync();
        });
    }

    public async Task RefreshAsync()
    {
        await _vocab.EnsureLoadedAsync();
        await _store.LoadAsync();

        var behavior = _packs.Active?.Behavior;

        _allRows.Clear();
        int seen = 0, known = 0, mastered = 0;
        var rows = new List<WordProgressRow>();

        foreach (var w in _vocab.All)
        {
            var p = _store.Get(w.Id);
            if (p.TotalSeen > 0) seen++;
            if (p.IsKnown)       known++;
            if (p.IsMastered)    mastered++;

            var ttsText = behavior?.TtsText(w) ?? w.FormAt(0);
            var row = new WordProgressRow
            {
                WordId      = w.Id,
                TtsText     = ttsText,
                Display     = behavior?.PrimaryForm(w) ?? w.FormAt(0),
                Reading     = behavior?.ReadingForm(w) ?? string.Empty,
                Meaning     = w.MeaningsJoined,
                Overall     = p.Overall,
                OverallText = $"{p.Overall:0}%",
                OverallColor = ColorFor(p.Overall),
            };
            row.SpeakCommand      = new Command(async () => await _tts.SpeakAsync(row.TtsText, row.WordId));
            row.AdjustUpCommand   = new Command(async () => await AdjustRowAsync(row, +25));
            row.AdjustDownCommand = new Command(async () => await AdjustRowAsync(row, -25));
            rows.Add(row);
        }

        foreach (var r in rows.OrderByDescending(r => r.Overall))
            _allRows.Add(r);

        Total    = _vocab.All.Count;
        Seen     = seen;
        Known    = known;
        Mastered = mastered;
        Summary  = string.Format(_loc["progress_summary_format"], Mastered, Total, Known, Seen);

        RebuildChartValues();
        ApplyFilter();
    }

    private async Task AdjustRowAsync(WordProgressRow row, double delta)
    {
        // Capture old proficiency state for incremental stat update.
        var oldP = _store.Get(row.WordId);
        bool wasKnown     = oldP.IsKnown;
        bool wasMastered  = oldP.IsMastered;

        await _store.AdjustScoresAsync(row.WordId, delta);
        var p = _store.Get(row.WordId);
        row.Overall      = p.Overall;
        row.OverallText  = $"{p.Overall:0}%";
        row.OverallColor = ColorFor(p.Overall);

        // Incrementally adjust Known / Mastered counts.
        if (!wasKnown    && p.IsKnown)    Known++;
        else if (wasKnown    && !p.IsKnown)    Known--;
        if (!wasMastered && p.IsMastered) Mastered++;
        else if (wasMastered && !p.IsMastered) Mastered--;
        Summary = string.Format(_loc["progress_summary_format"], Mastered, Total, Known, Seen);

        RebuildChartValues();
    }

    private void RebuildChartValues()
    {
        // _allRows is already sorted descending by Overall (set during RefreshAsync).
        // Individual adjust calls update row.Overall in-place; chart reflects current values.
        ChartValues = _allRows.Select(r => r.Overall).ToList();
    }

    private void ApplyFilter()
    {
        var query = _searchText.Trim();
        FilteredRows.Clear();
        foreach (var r in _allRows)
        {
            if (string.IsNullOrEmpty(query)
                || r.Display.Contains(query, StringComparison.OrdinalIgnoreCase)
                || r.Reading.Contains(query, StringComparison.OrdinalIgnoreCase)
                || r.Meaning.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                FilteredRows.Add(r);
            }
        }
    }

    private static Color ColorFor(double overall)
    {
        if (overall >= 85) return Color.FromArgb("#4CAF7A");
        if (overall >= 60) return Color.FromArgb("#7BC47F");
        if (overall >= 35) return Color.FromArgb("#FFB86B");
        if (overall > 0)   return Color.FromArgb("#E5556B");
        return Color.FromArgb("#7A7A8C");
    }
}

