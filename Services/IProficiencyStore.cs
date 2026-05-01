using LearnJP.Models;

namespace LearnJP.Services;

public interface IProficiencyStore
{
    Task LoadAsync();
    Task SaveAsync();
    WordProficiency Get(string wordId);
    IEnumerable<WordProficiency> All();
    Task RecordAsync(string wordId, ProficiencyCriterion criterion, bool correct);
    Task ResetAsync();
}
