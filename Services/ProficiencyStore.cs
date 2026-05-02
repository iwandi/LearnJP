using System.Text.Json;
using LearnJP.Models;

namespace LearnJP.Services;

public sealed class ProficiencyStore : IProficiencyStore
{
    private const string TurnsPrefKey = "proficiency.turns_asked";

    private readonly SemaphoreSlim _gate = new(1, 1);
    private Dictionary<string, WordProficiency> _byWord = new(StringComparer.Ordinal);
    private bool _loaded;
    private string? _filePath;
    private int _turnsAsked;

    public int TurnsAsked => _turnsAsked;

    private string GetFilePath()
    {
        if (_filePath is not null) return _filePath;
        string dir;
        try { dir = FileSystem.AppDataDirectory; }
        catch { dir = Path.Combine(Path.GetTempPath(), "LearnJP"); }
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
                _byWord = new(StringComparer.Ordinal);
            }

            try { _turnsAsked = Preferences.Default.Get(TurnsPrefKey, 0); }
            catch { _turnsAsked = 0; }

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

    public async Task SetReinforcedAsync(string wordId, bool reinforced)
    {
        var p = Get(wordId);
        if (p.IsReinforced == reinforced) return;
        p.IsReinforced = reinforced;
        // When pinning, make it due immediately so it surfaces on the next pick.
        if (reinforced) p.NextDueAtTurn = _turnsAsked;
        await SaveAsync();
    }

    public async Task RecordAsync(string wordId, ProficiencyCriterion criterion, bool correct)
    {
        var p = Get(wordId);
        p.RecordResult(criterion, correct);
        _turnsAsked++;
        p.NextDueAtTurn = _turnsAsked + ComputeInterval(p.Overall, correct);

        try { Preferences.Default.Set(TurnsPrefKey, _turnsAsked); } catch { /* ignore */ }
        await SaveAsync();
    }

    /// <summary>Number of upcoming questions before this word should be a candidate again.</summary>
    private static int ComputeInterval(double overall, bool correct)
    {
        if (!correct) return Random.Shared.Next(1, 3);            // 1..2 turns out
        // Smooth growth: 0% → ~2, 50% → ~14, 80% → ~52, 95% → ~110, 100% → ~140
        var raw = 2.0 * Math.Pow(1.6, overall / 12.0);
        // Cap so very high proficiency doesn't push a word effectively out of rotation.
        return (int)Math.Clamp(Math.Round(raw), 2, 250);
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
            _turnsAsked = 0;
            try { Preferences.Default.Set(TurnsPrefKey, 0); } catch { /* ignore */ }
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
