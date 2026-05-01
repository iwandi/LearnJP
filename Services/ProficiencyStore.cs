using System.Text.Json;
using LearnJP.Models;

namespace LearnJP.Services;

public sealed class ProficiencyStore : IProficiencyStore
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Dictionary<string, WordProficiency> _byWord = new(StringComparer.Ordinal);
    private bool _loaded;
    private string? _filePath;

    private string GetFilePath()
    {
        if (_filePath is not null) return _filePath;
        string dir;
        try { dir = FileSystem.AppDataDirectory; }
        catch
        {
            // Fallback for unpackaged / sandbox scenarios where ApplicationData isn't available.
            dir = Path.Combine(Path.GetTempPath(), "LearnJP");
        }
        try { Directory.CreateDirectory(dir); } catch { /* ignore */ }
        _filePath = Path.Combine(dir, "proficiency.json");
        return _filePath;
    }

    public async Task LoadAsync()
    {
        if (_loaded) return;
        await _gate.WaitAsync();
        try
        {
            if (_loaded) return;
            var path = GetFilePath();
            try
            {
                if (File.Exists(path))
                {
                    await using var fs = File.OpenRead(path);
                    var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    var list = await JsonSerializer.DeserializeAsync<List<WordProficiency>>(fs, opts) ?? new();
                    _byWord = list.ToDictionary(p => p.WordId, StringComparer.Ordinal);
                }
            }
            catch
            {
                // Corrupt or unreadable: start fresh; saving later will overwrite.
                _byWord = new(StringComparer.Ordinal);
            }
            _loaded = true;
        }
        finally { _gate.Release(); }
    }

    public WordProficiency Get(string wordId)
    {
        if (_byWord.TryGetValue(wordId, out var p)) return p;
        var fresh = new WordProficiency { WordId = wordId };
        _byWord[wordId] = fresh;
        return fresh;
    }

    public IEnumerable<WordProficiency> All() => _byWord.Values;

    public async Task RecordAsync(string wordId, ProficiencyCriterion criterion, bool correct)
    {
        var p = Get(wordId);
        p.RecordResult(criterion, correct);
        await SaveAsync();
    }

    public async Task SaveAsync()
    {
        await _gate.WaitAsync();
        try
        {
            try
            {
                var path = GetFilePath();
                await using var fs = File.Create(path);
                var opts = new JsonSerializerOptions { WriteIndented = false };
                await JsonSerializer.SerializeAsync(fs, _byWord.Values.ToList(), opts);
            }
            catch { /* persistence is best-effort; in-memory state remains */ }
        }
        finally { _gate.Release(); }
    }

    public async Task ResetAsync()
    {
        await _gate.WaitAsync();
        try
        {
            _byWord.Clear();
            try
            {
                var path = GetFilePath();
                if (File.Exists(path)) File.Delete(path);
            }
            catch { /* ignore */ }
        }
        finally { _gate.Release(); }
    }
}
