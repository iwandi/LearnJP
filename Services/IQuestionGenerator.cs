using LearnJP.Models;

namespace LearnJP.Services;

public interface IQuestionGenerator
{
    Task<Question?> NextAsync(LearningStrategy strategy = LearningStrategy.Neutral);

    /// <summary>The active "new term" frontier — never-seen words in the current pool, ordered
    /// by FrequencyRank, capped to the intake size. Updated on each <see cref="NextAsync"/> call.</summary>
    IReadOnlyList<Word> CurrentNewTermFrontier { get; }
}
