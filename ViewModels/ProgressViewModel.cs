using System.Collections.ObjectModel;
using System.Windows.Input;
using LearnJP.Models;
using LearnJP.Services;

namespace LearnJP.ViewModels;

public sealed class WordProgressRow
{
    public required string Display { get; init; }
    public required string Reading { get; init; }
    public required string Meaning { get; init; }
    public required double Overall { get; init; }
    public required string OverallText { get; init; }
    public required string CriterionBreakdown { get; init; }
    public required Color OverallColor { get; init; }
}

public sealed class ProgressViewModel : BaseViewModel
{
    private readonly IVocabularyService _vocab;
    private readonly IProficiencyStore _store;
    private readonly ILanguagePackService _packs;
    private readonly ILocalizationService _loc;

    private string _summary = "";
    private int _total;
    private int _seen;
    private int _known;
    private int _mastered;

    public ILocalizationService Loc => _loc;

    public ObservableCollection<WordProgressRow> Rows { get; } = new();

    public string Summary { get => _summary; private set => SetProperty(ref _summary, value); }
    public int Total { get => _total; private set => SetProperty(ref _total, value); }
    public int Seen { get => _seen; private set => SetProperty(ref _seen, value); }
    public int Known { get => _known; private set => SetProperty(ref _known, value); }
    public int Mastered { get => _mastered; private set => SetProperty(ref _mastered, value); }

    public ICommand RefreshCommand { get; }
    public ICommand ResetCommand { get; }

    public ProgressViewModel(IVocabularyService vocab, IProficiencyStore store, ILanguagePackService packs, ILocalizationService loc)
    {
        _vocab = vocab;
        _store = store;
        _packs = packs;
        _loc = loc;
        RefreshCommand = new Command(async () => await RefreshAsync());
        ResetCommand = new Command(async () =>
        {
            await _store.ResetAsync();
            await RefreshAsync();
        });
    }

    public async Task RefreshAsync()
    {
        await _vocab.EnsureLoadedAsync();
        await _store.LoadAsync();

        // Display/reading rendering goes through the active language's behaviour so this
        // page works for any pack — JP gets "kanji / kana", Italian gets "ciao / ".
        var behavior = _packs.Active?.Behavior;

        Rows.Clear();
        int seen = 0, known = 0, mastered = 0;
        var rows = new List<WordProgressRow>();
        foreach (var w in _vocab.All)
        {
            var p = _store.Get(w.Id);
            if (p.TotalSeen > 0) seen++;
            if (p.IsKnown) known++;
            if (p.IsMastered) mastered++;

            var breakdown = string.Join("  ",
                ProficiencyCriterionExtensions.All.Select(c => $"{ShortLabel(c)} {p.GetScore(c):0}"));

            rows.Add(new WordProgressRow
            {
                Display = behavior?.PrimaryForm(w) ?? w.FormAt(0),
                Reading = behavior?.ReadingForm(w) ?? string.Empty,
                Meaning = w.MeaningsJoined,
                Overall = p.Overall,
                OverallText = $"{p.Overall:0}%",
                CriterionBreakdown = breakdown,
                OverallColor = ColorFor(p.Overall)
            });
        }

        foreach (var r in rows.OrderByDescending(r => r.Overall))
            Rows.Add(r);

        Total = _vocab.All.Count;
        Seen = seen;
        Known = known;
        Mastered = mastered;
        Summary = string.Format(_loc["progress_summary_format"], Mastered, Total, Known, Seen);
    }

    private static string ShortLabel(ProficiencyCriterion c) => c switch
    {
        ProficiencyCriterion.TargetToBase => "T→B",
        ProficiencyCriterion.BaseToTarget => "B→T",
        ProficiencyCriterion.SimilarSoundDifferentiation => "Snd",
        ProficiencyCriterion.SimilarMeaningDifferentiation => "Mng",
        _ => "?"
    };

    private static Color ColorFor(double overall)
    {
        if (overall >= 85) return Color.FromArgb("#4CAF7A");
        if (overall >= 60) return Color.FromArgb("#7BC47F");
        if (overall >= 35) return Color.FromArgb("#FFB86B");
        if (overall > 0)   return Color.FromArgb("#E5556B");
        return Color.FromArgb("#7A7A8C");
    }
}
