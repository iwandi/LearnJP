namespace LearnJP.Models;

public enum LearningStrategy
{
    Neutral,
    Spaced,
    QuickReview,
    WeakFocus,
    Fsrs
}

public static class LearningStrategyExtensions
{
    public static string Display(this LearningStrategy s) => s switch
    {
        LearningStrategy.Neutral     => "Neutral",
        LearningStrategy.Spaced      => "Spaced (turn-based)",
        LearningStrategy.QuickReview => "Quick review (known)",
        LearningStrategy.WeakFocus   => "Weak focus (drill)",
        LearningStrategy.Fsrs        => "FSRS (retrievability-targeted)",
        _ => s.ToString()
    };
}
