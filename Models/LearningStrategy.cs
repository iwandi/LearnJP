namespace LearnJP.Models;

public enum LearningStrategy
{
    Neutral,
    Spaced,
    QuickReview,
    WeakFocus
}

public static class LearningStrategyExtensions
{
    public static string Display(this LearningStrategy s) => s switch
    {
        LearningStrategy.Neutral     => "Neutral",
        LearningStrategy.Spaced      => "Spaced (turn-based)",
        LearningStrategy.QuickReview => "Quick review (known)",
        LearningStrategy.WeakFocus   => "Weak focus (drill)",
        _ => s.ToString()
    };
}
