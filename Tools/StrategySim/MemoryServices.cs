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
    private readonly int _intervalCap;
    private int _turns;

    public MemoryProficiencyStore(int intervalCap = 250) { _intervalCap = intervalCap; }

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

    public Task RecordAsync(string wordId, ProficiencyCriterion criterion, bool correct, int elapsedMs = 0)
    {
        var p = Get(wordId);
        p.RecordResult(criterion, correct);
        _turns++;
        p.NextDueAtTurn = _turns + ComputeInterval(p.Overall, correct, _intervalCap);

        // FSRS state update — z-score from the running response-time aggregates.
        var grade = Fsrs.GradeFromTime(correct, ZScoreFromMs(elapsedMs));
        var prev = _fsrsStates.TryGetValue(wordId, out var existing) ? existing : default;
        _fsrsStates[wordId] = Fsrs.Update(prev, grade, DateTime.UtcNow);

        if (correct && elapsedMs > 0)
        {
            _timeCount++;
            _timeSum += elapsedMs;
            _timeSumSq += (double)elapsedMs * elapsedMs;
        }
        return Task.CompletedTask;
    }

    private readonly Dictionary<string, FsrsState> _fsrsStates = new(StringComparer.Ordinal);
    private long _timeCount;
    private double _timeSum, _timeSumSq;

    private double ZScoreFromMs(int elapsedMs)
    {
        if (elapsedMs <= 0 || _timeCount < 8) return 0;
        var mean = _timeSum / _timeCount;
        var variance = (_timeSumSq / _timeCount) - mean * mean;
        var stddev = variance > 0 ? Math.Sqrt(variance) : 0;
        if (stddev < 1) return 0;
        return (elapsedMs - mean) / stddev;
    }

    public FsrsState GetFsrsState(string wordId) =>
        _fsrsStates.TryGetValue(wordId, out var s) ? s : default;

    public IEnumerable<(string WordId, FsrsState State)> AllFsrsStates()
    {
        foreach (var (id, s) in _fsrsStates) yield return (id, s);
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
        _confusions.Clear();
        return Task.CompletedTask;
    }

    private readonly Dictionary<(string Target, string Picked), int> _confusions = new();

    public Task RecordConfusionAsync(string targetId, string pickedId)
    {
        if (string.IsNullOrEmpty(targetId) || string.IsNullOrEmpty(pickedId)) return Task.CompletedTask;
        var key = (targetId, pickedId);
        _confusions[key] = _confusions.TryGetValue(key, out var c) ? c + 1 : 1;
        return Task.CompletedTask;
    }

    public IReadOnlyList<(string PickedId, int Count)> GetTopConfusersFor(string targetId, int limit)
    {
        if (limit <= 0) return Array.Empty<(string, int)>();
        var hits = new List<(string PickedId, int Count)>();
        foreach (var ((t, p), n) in _confusions)
            if (t == targetId) hits.Add((p, n));
        hits.Sort((a, b) => b.Count.CompareTo(a.Count));
        return hits.Count > limit ? hits.GetRange(0, limit) : hits;
    }

    public IReadOnlyCollection<string> GetConfusedTargetIds()
    {
        var s = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (t, _) in _confusions.Keys) s.Add(t);
        return s;
    }

    // Mirrors ProficiencyStore.ComputeInterval — keep in sync. The cap is parameterised here
    // (production hard-codes 250) so the simulator can sweep it.
    private static int ComputeInterval(double overall, bool correct, int cap)
    {
        if (!correct) return Random.Shared.Next(1, 3);
        var raw = 2.0 * Math.Pow(1.6, overall / 12.0);
        return (int)Math.Clamp(Math.Round(raw), 2, cap);
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
    public LearningStrategy SelectedLearningStrategy { get; set; } = LearningStrategy.Fsrs;
    public bool CountForProficiency { get; set; } = true;
}
