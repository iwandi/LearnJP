using LearnJP.Models;

namespace LearnJP.Tools.StrategySim;

internal interface IAnswerBot
{
    string Name { get; }
    int PickOptionIndex(Question q, Random rng);
}

internal sealed class RandomBot : IAnswerBot
{
    public string Name => "random";
    public int PickOptionIndex(Question q, Random rng) => rng.Next(q.Options.Count);
}

internal sealed class AlwaysRightBot : IAnswerBot
{
    public string Name => "always-right";
    public int PickOptionIndex(Question q, Random rng) => IndexOfCorrect(q);
    public static int IndexOfCorrect(Question q)
    {
        for (int i = 0; i < q.Options.Count; i++) if (q.Options[i].IsCorrect) return i;
        return 0;
    }
}

internal sealed class AlwaysWrongBot : IAnswerBot
{
    public string Name => "always-wrong";
    public int PickOptionIndex(Question q, Random rng)
    {
        for (int i = 0; i < q.Options.Count; i++) if (!q.Options[i].IsCorrect) return i;
        return 0;
    }
}

/// <summary>Picks the right answer with probability p, otherwise a uniform-random distractor.</summary>
internal sealed class ChanceBot : IAnswerBot
{
    private readonly double _p;
    public ChanceBot(double p) { _p = Math.Clamp(p, 0, 1); }
    public string Name => $"chance-{_p:F2}";
    public int PickOptionIndex(Question q, Random rng)
    {
        if (rng.NextDouble() < _p) return AlwaysRightBot.IndexOfCorrect(q);
        var wrong = new List<int>();
        for (int i = 0; i < q.Options.Count; i++) if (!q.Options[i].IsCorrect) wrong.Add(i);
        return wrong.Count == 0 ? 0 : wrong[rng.Next(wrong.Count)];
    }
}

/// <summary>
/// Locks in a word after K exposures: every time the bot sees a particular word for the first
/// K-1 times it answers wrong; from the K-th encounter onward it answers right. Models the
/// "I needed to see it a few times before it stuck" learner profile that pure probability
/// bots can't reproduce.
/// </summary>
internal sealed class StreakBot : IAnswerBot
{
    private readonly int _k;
    private readonly Dictionary<string, int> _seen = new(StringComparer.Ordinal);
    public StreakBot(int k) { _k = Math.Max(1, k); }
    public string Name => $"streak-{_k}";

    public int PickOptionIndex(Question q, Random rng)
    {
        var id = q.Target.Id;
        var n = _seen[id] = _seen.TryGetValue(id, out var c) ? c + 1 : 1;
        if (n >= _k) return AlwaysRightBot.IndexOfCorrect(q);
        var wrong = new List<int>();
        for (int i = 0; i < q.Options.Count; i++) if (!q.Options[i].IsCorrect) wrong.Add(i);
        return wrong.Count == 0 ? 0 : wrong[rng.Next(wrong.Count)];
    }
}

/// <summary>
/// Models a learner: the chance of answering correctly is a function of the word's current
/// proficiency. At proficiency 0 the bot guesses (1/options); at 100 it's correct ~99% of
/// the time. Useful for validating that a strategy actually drives proficiency upward over
/// many turns rather than asking new words at random forever.
/// </summary>
internal sealed class LearnerBot : IAnswerBot
{
    private readonly Func<string, double> _proficiencyOf;
    public LearnerBot(Func<string, double> proficiencyOf) { _proficiencyOf = proficiencyOf; }
    public string Name => "learner";

    public int PickOptionIndex(Question q, Random rng)
    {
        var prof = _proficiencyOf(q.Target.Id);
        var guessFloor = 1.0 / Math.Max(2, q.Options.Count);
        // Blend from guess-floor at prof=0 to ~0.99 at prof=100.
        var pCorrect = guessFloor + (0.99 - guessFloor) * (prof / 100.0);
        if (rng.NextDouble() < pCorrect) return AlwaysRightBot.IndexOfCorrect(q);
        var wrong = new List<int>();
        for (int i = 0; i < q.Options.Count; i++) if (!q.Options[i].IsCorrect) wrong.Add(i);
        return wrong.Count == 0 ? 0 : wrong[rng.Next(wrong.Count)];
    }
}
