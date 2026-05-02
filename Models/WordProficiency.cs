using System.Text.Json.Serialization;

namespace LearnJP.Models;

public sealed class WordProficiency
{
    [JsonPropertyName("wordId")]
    public string WordId { get; set; } = string.Empty;

    [JsonPropertyName("scores")]
    public Dictionary<ProficiencyCriterion, double> Scores { get; set; } = new();

    [JsonPropertyName("attempts")]
    public Dictionary<ProficiencyCriterion, int> Attempts { get; set; } = new();

    [JsonPropertyName("lastSeenUtc")]
    public DateTime? LastSeenUtc { get; set; }

    [JsonPropertyName("totalSeen")]
    public int TotalSeen { get; set; }

    [JsonPropertyName("totalCorrect")]
    public int TotalCorrect { get; set; }

    /// <summary>Global turn counter at which this word becomes a candidate again under spaced-repetition strategies.</summary>
    [JsonPropertyName("nextDueAtTurn")]
    public int NextDueAtTurn { get; set; }

    public double GetScore(ProficiencyCriterion c) =>
        Scores.TryGetValue(c, out var v) ? v : 0.0;

    public int GetAttempts(ProficiencyCriterion c) =>
        Attempts.TryGetValue(c, out var v) ? v : 0;

    public double Overall
    {
        get
        {
            var all = ProficiencyCriterionExtensions.All;
            double sum = 0;
            foreach (var c in all) sum += GetScore(c);
            return sum / all.Count;
        }
    }

    public bool IsKnown => Overall >= 60.0;
    public bool IsMastered => ProficiencyCriterionExtensions.All.All(c => GetScore(c) >= 85.0);

    public void RecordResult(ProficiencyCriterion c, bool correct)
    {
        var current = GetScore(c);
        if (correct)
            current += (100.0 - current) * 0.25;
        else
            current *= 0.55;
        Scores[c] = Math.Clamp(current, 0, 100);
        Attempts[c] = GetAttempts(c) + 1;
        TotalSeen++;
        if (correct) TotalCorrect++;
        LastSeenUtc = DateTime.UtcNow;
    }
}
