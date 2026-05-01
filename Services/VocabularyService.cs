using System.Text.Json;
using LearnJP.Models;

namespace LearnJP.Services;

public sealed class VocabularyService : IVocabularyService
{
    private const string ResourceName = "vocabulary.json";

    private List<Word> _words = new();
    private Dictionary<string, Word> _byId = new();
    private bool _loaded;

    public IReadOnlyList<Word> All => _words;

    public Word? GetById(string id) => _byId.TryGetValue(id, out var w) ? w : null;

    public async Task EnsureLoadedAsync()
    {
        if (_loaded) return;
        await using var stream = await FileSystem.OpenAppPackageFileAsync(ResourceName);
        var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var loaded = await JsonSerializer.DeserializeAsync<List<Word>>(stream, opts) ?? new();
        _words = loaded;
        _byId = _words.Where(w => !string.IsNullOrEmpty(w.Id)).ToDictionary(w => w.Id);
        _loaded = true;
    }
}
