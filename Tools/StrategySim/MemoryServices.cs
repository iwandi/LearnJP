using System.Text.Json;
using LearnJP.Models;
using LearnJP.Services;

namespace LearnJP.Tools.StrategySim;

/// <summary>Loads vocabulary.json from disk; supports an optional cap on pool size.</summary>
internal sealed class MemoryVocabularyService : IVocabularyService
{
    private readonly string _path;
    private readonly int? _limit;
    private List<Word> _words = new();

    public MemoryVocabularyService(string path, int? limit)
    {
        _path = path;
        _limit = limit;
    }

    public IReadOnlyList<Word> All => _words;

    public Task EnsureLoadedAsync()
    {
        if (_words.Count > 0) return Task.CompletedTask;
        var json = File.ReadAllText(_path);
        var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var list = JsonSerializer.Deserialize<List<Word>>(json, opts) ?? new();
        if (_limit is { } cap) list = list.Take(cap).ToList();
        _words = list;
        return Task.CompletedTask;
    }

    public Word? GetById(string id) => _words.FirstOrDefault(w => w.Id == id);
}

/// <summary>
/// Pure in-memory store. Must replicate the proficiency math from
/// <see cref="ProficiencyStore"/> (RecordResult lives on the model; ComputeInterval is mirrored
/// here — keep the two in sync).
/// </summary>
internal sealed class MemoryProficiencyStore : IProficiencyStore
{
    private readonly Dictionary<string, WordProficiency> _byWord = new(StringComparer.Ordinal);
    private int _turns;

    public int TurnsAsked => _turns;

    public Task LoadAsync() => Task.CompletedTask;
    public Task SaveAsync() => Task.CompletedTask;

    public WordProficiency Get(string wordId)
    {
        if (_byWord.TryGetValue(wordId, out var p)) return p;
        var fresh = new WordProficiency { WordId = wordId };
        _byWord[wordId] = fresh;
        return fresh;
    }

    public IEnumerable<WordProficiency> All() => _byWord.Values;

    public Task RecordAsync(string wordId, ProficiencyCriterion criterion, bool correct)
    {
        var p = Get(wordId);
        p.RecordResult(criterion, correct);
        _turns++;
        p.NextDueAtTurn = _turns + ComputeInterval(p.Overall, correct);
        return Task.CompletedTask;
    }

    public Task SetReinforcedAsync(string wordId, bool reinforced)
    {
        var p = Get(wordId);
        p.IsReinforced = reinforced;
        if (reinforced) p.NextDueAtTurn = _turns;
        return Task.CompletedTask;
    }

    public Task ResetAsync()
    {
        _byWord.Clear();
        _turns = 0;
        return Task.CompletedTask;
    }

    // Mirrors ProficiencyStore.ComputeInterval — keep in sync.
    private static int ComputeInterval(double overall, bool correct)
    {
        if (!correct) return Random.Shared.Next(1, 3);
        var raw = 2.0 * Math.Pow(1.6, overall / 12.0);
        return (int)Math.Clamp(Math.Round(raw), 2, 250);
    }
}

/// <summary>Minimal settings impl backed by simple fields — no Preferences, no MAUI.</summary>
internal sealed class MemorySettingsService : ISettingsService
{
    public bool RomajiOnly { get; set; }
    public bool TtsEnabled { get; set; } = true;
    public bool ForceFurigana { get; set; }
    public double TtsRate { get; set; } = 0.9;
    public TtsProvider TtsProvider { get; set; } = TtsProvider.System;
    public string AzureSpeechKey { get; set; } = string.Empty;
    public string AzureSpeechRegion { get; set; } = string.Empty;
    public string AzureJapaneseVoice { get; set; } = string.Empty;
    public string AzureEnglishVoice { get; set; } = string.Empty;
    public double SystemTtsVolume { get; set; } = 1.0;
    public double AzureTtsVolume { get; set; } = 1.0;
    public IReadOnlyList<string> ActiveIncludeTags { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> ActiveExcludeTags { get; set; } = Array.Empty<string>();
    public LearningStrategy SelectedLearningStrategy { get; set; } = LearningStrategy.Spaced;
    public bool CountForProficiency { get; set; } = true;
}
