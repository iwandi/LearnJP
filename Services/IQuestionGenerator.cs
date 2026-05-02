using LearnJP.Models;

namespace LearnJP.Services;

public interface IQuestionGenerator
{
    Task<Question?> NextAsync(LearningStrategy strategy = LearningStrategy.Neutral);
}
