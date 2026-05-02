namespace LearnJP.Models;

public enum LearningStrategy
{
    Neutral,
    Spaced
}

public static class LearningStrategyExtensions
{
    public static string Display(this LearningStrategy s) => s switch
    {
        LearningStrategy.Neutral => "Neutral",
        LearningStrategy.Spaced  => "Spaced (turn-based)",
        _ => s.ToString()
    };
}
