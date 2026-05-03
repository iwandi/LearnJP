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

        // Read the vocabulary file as a flat JsonDocument so we can project each entry through
        // the pack's declared form-key list. This is what keeps Word.Forms language-neutral —
        // the runtime model holds positional values, the JSON keeps human-friendly key names.
        await using (var stream = await FileSystem.OpenAppPackageFileAsync(pack.VocabFile))
        using (var doc = await JsonDocument.ParseAsync(stream))
        {
            _words = LoadWords(doc.RootElement, pack.EffectiveForms);
        }

        // Translations into the user's base language. Joined onto each word by id; missing
        // entries leave Meanings empty (the algorithm handles empty meanings gracefully).
        var translationFile = pack.ResolveTranslationFile(baseId);
        if (!string.IsNullOrWhiteSpace(translationFile))
        {
            try
            {
                var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
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

    /// <summary>Projects a JSON array of word entries into runtime <see cref="Word"/> instances.
    /// Each entry's <see cref="Word.Forms"/> is filled in the order declared by
    /// <paramref name="formKeys"/> — missing keys become empty strings so position-by-index
    /// stays stable.</summary>
    private static List<Word> LoadWords(JsonElement root, IReadOnlyList<string> formKeys)
    {
        var words = new List<Word>();
        if (root.ValueKind != JsonValueKind.Array) return words;

        foreach (var el in root.EnumerateArray())
        {
            if (el.ValueKind != JsonValueKind.Object) continue;
            var w = new Word
            {
                Id            = ReadString(el, "id"),
                PartOfSpeech  = ReadString(el, "pos"),
                FrequencyRank = ReadInt(el, "frequencyRank", int.MaxValue),
                Meanings      = ReadStringList(el, "meanings"),
                Tags          = ReadStringList(el, "tags"),
                Forms         = ReadForms(el, formKeys),
            };
            words.Add(w);
        }
        return words;
    }

    private static string[] ReadForms(JsonElement el, IReadOnlyList<string> formKeys)
    {
        var arr = new string[formKeys.Count];
        for (int i = 0; i < formKeys.Count; i++)
            arr[i] = ReadString(el, formKeys[i]);
        return arr;
    }

    private static string ReadString(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? string.Empty
            : string.Empty;

    private static int ReadInt(JsonElement el, string name, int fallback) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n)
            ? n : fallback;

    private static List<string> ReadStringList(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.Array) return new();
        var list = new List<string>(v.GetArrayLength());
        foreach (var item in v.EnumerateArray())
            if (item.ValueKind == JsonValueKind.String)
                list.Add(item.GetString() ?? string.Empty);
        return list;
    }

    private sealed class TranslationEntry
    {
        [JsonPropertyName("id")]       public string Id { get; set; } = string.Empty;
        [JsonPropertyName("meanings")] public List<string> Meanings { get; set; } = new();
    }
}
