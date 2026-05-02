using LearnJP.Models;
using LearnJP.Services;

namespace LearnJP.Tools.StrategySim;

internal sealed class TurnRecord
{
    public required int Turn { get; init; }
    public required string WordId { get; init; }
    public required ProficiencyCriterion Criterion { get; init; }
    public required bool Correct { get; init; }
    public required double OverallAfter { get; init; }
    public required int FrontierSize { get; init; }
}

internal sealed class Analyzer
{
    private readonly List<TurnRecord> _records = new();
    private readonly Dictionary<string, int> _firstReachedKnown = new(); // wordId -> turn it crossed Overall>=60
    private readonly Dictionary<string, int> _picksPerWord = new(StringComparer.Ordinal);

    public void Record(TurnRecord r)
    {
        _records.Add(r);
        _picksPerWord[r.WordId] = _picksPerWord.TryGetValue(r.WordId, out var c) ? c + 1 : 1;
        if (r.OverallAfter >= 60.0 && !_firstReachedKnown.ContainsKey(r.WordId))
            _firstReachedKnown[r.WordId] = r.Turn;
    }

    public void Print(IProficiencyStore store, IReadOnlyList<Word> pool, string botName, LearningStrategy strategy, string? tunables = null)
    {
        var totalTurns = _records.Count;
        var correct = _records.Count(r => r.Correct);

        var profs = pool.Select(w => store.Get(w.Id)).ToList();
        var unseen   = profs.Count(p => p.TotalSeen == 0);
        var struggling = profs.Count(p => p.TotalSeen > 0 && p.Overall < 35);
        var learning = profs.Count(p => p.TotalSeen > 0 && p.Overall >= 35 && p.Overall < 70);
        var known    = profs.Count(p => p.TotalSeen > 0 && p.Overall >= 70 && !p.IsMastered);
        var mastered = profs.Count(p => p.IsMastered);

        var picks = _picksPerWord.Values.OrderBy(v => v).ToList();
        var pickP50 = picks.Count > 0 ? picks[picks.Count / 2] : 0;
        var pickP90 = picks.Count > 0 ? picks[Math.Min(picks.Count - 1, picks.Count * 9 / 10)] : 0;
        var pickMax = picks.Count > 0 ? picks[^1] : 0;
        // Average over the *full eligible pool*, not just touched words — matches the user
        // intuition "if I answer N times, how many picks does each word get on average?".
        var pickMeanAll = pool.Count > 0 ? (double)totalTurns / pool.Count : 0;
        var pickMeanTouched = picks.Count > 0 ? picks.Average() : 0;

        var ttk = _firstReachedKnown.Values.OrderBy(v => v).ToList();
        var ttkP50 = ttk.Count > 0 ? ttk[ttk.Count / 2].ToString() : "n/a";
        var ttkP90 = ttk.Count > 0 ? ttk[Math.Min(ttk.Count - 1, ttk.Count * 9 / 10)].ToString() : "n/a";

        // Average frontier size over the run + how often it actually changed members.
        var avgFrontier = _records.Count > 0 ? _records.Average(r => r.FrontierSize) : 0;

        Console.WriteLine();
        var suffix = string.IsNullOrEmpty(tunables) ? "" : $" / {tunables}";
        Console.WriteLine($"=== {botName} / {strategy} / pool={pool.Count}{suffix} ===");
        Console.WriteLine($"  turns asked       : {totalTurns}");
        Console.WriteLine($"  accuracy          : {(totalTurns == 0 ? 0 : 100.0 * correct / totalTurns):F1}%  ({correct}/{totalTurns})");
        Console.WriteLine($"  pool state        : unseen={unseen}  struggling={struggling}  learning={learning}  known={known}  mastered={mastered}");
        Console.WriteLine($"  picks per word    : mean(all)={pickMeanAll:F1}  mean(touched)={pickMeanTouched:F1}  p50={pickP50}  p90={pickP90}  max={pickMax}  unique-touched={_picksPerWord.Count}");
        Console.WriteLine($"  turns→known(≥60)  : reached={_firstReachedKnown.Count}/{pool.Count}  p50={ttkP50}  p90={ttkP90}");
        Console.WriteLine($"  avg frontier size : {avgFrontier:F1}");
    }

    public void DumpCsv(string path)
    {
        using var sw = new StreamWriter(path);
        sw.WriteLine("turn,word_id,criterion,correct,overall_after,frontier_size");
        foreach (var r in _records)
            sw.WriteLine($"{r.Turn},{r.WordId},{r.Criterion},{r.Correct},{r.OverallAfter:F2},{r.FrontierSize}");
    }
}
