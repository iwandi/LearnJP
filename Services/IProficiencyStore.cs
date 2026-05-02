using LearnJP.Models;

namespace LearnJP.Services;

public interface IProficiencyStore
{
    Task LoadAsync();
    Task SaveAsync();
    WordProficiency Get(string wordId);
    IEnumerable<WordProficiency> All();
    /// <summary>Monotonic counter incremented for every recorded answer.</summary>
    int TurnsAsked { get; }
    Task RecordAsync(string wordId, ProficiencyCriterion criterion, bool correct);
    Task ResetAsync();
}
