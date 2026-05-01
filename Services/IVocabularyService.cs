using LearnJP.Models;

namespace LearnJP.Services;

public interface IVocabularyService
{
    Task EnsureLoadedAsync();
    IReadOnlyList<Word> All { get; }
    Word? GetById(string id);
}
