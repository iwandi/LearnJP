using System.Text.Json;
using System.Text.Json.Serialization;
using LearnJP.Models;

namespace LearnJP.Services;

public sealed class VocabularyService : IVocabularyService
{
    private readonly ILanguagePackService _packs;
    private readonly ISettingsService _settings;
    private List<Word> _words = new();
    private Dictionary<string, Word> _byId = new();
    private string? _loadedKey;

    public VocabularyService(ILanguagePackService packs, ISettingsService settings)
    {
        _packs = packs;
        _settings = settings;
    }

    public IReadOnlyList<Word> All => _words;
    public Word? GetById(string id) => _byId.TryGetValue(id, out var w) ? w : null;

    public async Task EnsureLoadedAsync()
    {
        await _packs.EnsureLoadedAsync();
        var pack = _packs.Active;
        if (pack is null) return;
        var baseId = _settings.BaseLanguageId;
        var key = pack.Id + "::" + (baseId ?? string.Empty);
        if (_loadedKey == key) return;

        var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        // Target-language fields (kanji/kana/romaji + tags + frequency rank).
        await using (var stream = await FileSystem.OpenAppPackageFileAsync(pack.VocabFile))
        {
            _words = await JsonSerializer.DeserializeAsync<List<Word>>(stream, opts) ?? new();
        }

        // Translations into the user's base language. Joined onto each word by id; missing
        // entries leave Meanings empty (the algorithm handles empty meanings gracefully).
        var translationFile = pack.ResolveTranslationFile(baseId);
        if (!string.IsNullOrWhiteSpace(translationFile))
        {
            try
            {
                await using var stream = await FileSystem.OpenAppPackageFileAsync(translationFile);
                var translations = await JsonSerializer.DeserializeAsync<List<TranslationEntry>>(stream, opts) ?? new();
                var byId = translations.Where(t => !string.IsNullOrEmpty(t.Id))
                                       .ToDictionary(t => t.Id, t => t.Meanings);
                foreach (var w in _words)
                    if (byId.TryGetValue(w.Id, out var meanings)) w.Meanings = meanings;
            }
            catch { /* best effort — words just don't get meanings */ }
        }

        _byId = _words.Where(w => !string.IsNullOrEmpty(w.Id)).ToDictionary(w => w.Id);
        _loadedKey = key;
    }

    private sealed class TranslationEntry
    {
        [JsonPropertyName("id")]       public string Id { get; set; } = string.Empty;
        [JsonPropertyName("meanings")] public List<string> Meanings { get; set; } = new();
    }
}
