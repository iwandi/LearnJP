using LearnJP.Models;

namespace LearnJP.Services;

/// <summary>Per-word FSRS state. Stored as columns on the proficiency_meta table.</summary>
public struct FsrsState
{
    public double Stability;          // expected interval (days) until R drops to ~0.5
    public double Difficulty;         // 1..10, higher = harder for this user
    public DateTime? LastReviewUtc;

    public bool HasState => Stability > 0 && LastReviewUtc.HasValue;
}

/// <summary>
/// FSRS-lite: same shape as the Free Spaced Repetition Scheduler (per-card stability and
/// difficulty + retrievability-targeted scheduling), but with hand-tuned coefficients so we
/// can ship before fitting real weights against user data. Swap <see cref="Update"/> with a
/// proper FSRS-4 port once we have a logged review corpus.
/// </summary>
public static class Fsrs
{
    public enum Grade { Again = 1, Hard = 2, Good = 3, Easy = 4 }

    /// <summary>Target retrievability used by the picker — pick the word whose R is closest below.</summary>
    public const double TargetRetrievability = 0.90;

    /// <summary>R(t) = (1 + t / (9·S))^-1 — FSRS-4.5 forgetting curve.</summary>
    public static double Retrievability(FsrsState s, DateTime now)
    {
        if (!s.HasState) return 0;
        var t = Math.Max(0.0, (now - s.LastReviewUtc!.Value).TotalDays);
        return Math.Pow(1.0 + t / (9.0 * Math.Max(0.1, s.Stability)), -1);
    }

    /// <summary>Maps correctness + a z-scored response latency to a 4-level grade.</summary>
    public static Grade GradeFromTime(bool correct, double zScore)
    {
        if (!correct) return Grade.Again;
        if (zScore < -0.5) return Grade.Easy;
        if (zScore >  0.5) return Grade.Hard;
        return Grade.Good;
    }

    /// <summary>
    /// Returns the new state after a review at <paramref name="now"/> with the given grade.
    /// Hand-tuned coefficients; the shape mirrors FSRS but the constants are deliberately
    /// conservative so a small dataset can't blow stability up or down too aggressively.
    /// </summary>
    public static FsrsState Update(FsrsState prev, Grade g, DateTime now)
    {
        // First review: seed S and D from the grade.
        if (!prev.HasState)
        {
            return new FsrsState
            {
                Stability = g switch
                {
                    Grade.Easy => 8.0,
                    Grade.Good => 4.0,
                    Grade.Hard => 2.0,
                    _          => 1.0  // Again — review again tomorrow
                },
                Difficulty = g switch
                {
                    Grade.Easy => 3.0,
                    Grade.Good => 5.0,
                    Grade.Hard => 6.5,
                    _          => 8.0
                },
                LastReviewUtc = now
            };
        }

        var r = Retrievability(prev, now);
        var s = prev.Stability;
        var d = prev.Difficulty;

        // Difficulty drifts by grade: easier ratings reduce D, harder/wrong increases it.
        var dDelta = g switch
        {
            Grade.Easy => -1.0,
            Grade.Good => -0.2,
            Grade.Hard =>  0.5,
            _          =>  1.5
        };
        var newD = Math.Clamp(d + dDelta, 1.0, 10.0);

        double newS;
        if (g == Grade.Again)
        {
            // Forgetting penalty: collapse stability; never increase. Floor at 0.5 so the
            // word becomes due imminently rather than vanishing.
            newS = Math.Max(0.5, s * 0.3);
        }
        else
        {
            // Recall: grow stability. Bigger jumps when the word was hard to recall (low R)
            // and the user's per-word difficulty is low (well-learned items grow faster).
            var difficultyFactor      = (11.0 - newD) / 10.0;          // 0.1..1.0
            var retrievabilityFactor  = 1.0 + (1.0 - r) * 2.0;          // 1.0..3.0
            var gradeMultiplier       = g switch
            {
                Grade.Easy => 1.5,
                Grade.Good => 1.0,
                Grade.Hard => 0.7,
                _          => 0.5
            };
            newS = s * (1.0 + difficultyFactor * retrievabilityFactor * gradeMultiplier);
            // Cap so a single perfect review can't push a word out beyond ~250 days.
            newS = Math.Min(newS, 250.0);
        }

        return new FsrsState
        {
            Stability = Math.Max(0.5, newS),
            Difficulty = newD,
            LastReviewUtc = now
        };
    }
}
