using System.Diagnostics;
using System.Text.Json;
using LearnJP.Models;
using Microsoft.Data.Sqlite;

namespace LearnJP.Services;

/// <summary>
/// SQLite-backed proficiency + confusion-matrix store. Lazy-loads per-word proficiency on
/// first access and caches it in memory for reuse; confusion data is loaded eagerly (small)
/// and write-through. The store can be queried directly via the underlying database for
/// analytics; the in-memory cache is a convenience for the hot per-question path.
/// </summary>
public sealed class SqliteProficiencyStore : IProficiencyStore
{
    private const string TurnsMetaKey = "turns_asked";

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, WordProficiency> _cache = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Target, string Picked), int> _confusions = new();
    private readonly Dictionary<string, FsrsState> _fsrs = new(StringComparer.Ordinal);

    // Running aggregates of correct-answer response time (across all criteria), persisted to
    // the meta table. Used to z-score the next answer for FSRS time→grade mapping.
    private long _timeCount;
    private double _timeSum;
    private double _timeSumSq;

    private string? _dbPath;
    private bool _loaded;
    private int _turnsAsked;

    public int TurnsAsked => _turnsAsked;

    private string GetDbPath()
    {
        if (_dbPath is not null) return _dbPath;
        string dir;
        try { dir = FileSystem.AppDataDirectory; }
        catch { dir = Path.Combine(Path.GetTempPath(), "LearnJP"); }
        try { Directory.CreateDirectory(dir); } catch { /* ignore */ }
        _dbPath = Path.Combine(dir, "proficiency.db");
        return _dbPath;
    }

    private SqliteConnection OpenConnection()
    {
        var conn = new SqliteConnection($"Data Source={GetDbPath()}");
        conn.Open();
        return conn;
    }

    public async Task LoadAsync()
    {
        if (_loaded) return;
        await _gate.WaitAsync();
        try
        {
            if (_loaded) return;

            using (var conn = OpenConnection())
            {
                EnsureSchema(conn);

                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT value FROM meta WHERE key = $k";
                    cmd.Parameters.AddWithValue("$k", TurnsMetaKey);
                    var raw = cmd.ExecuteScalar() as string;
                    _turnsAsked = int.TryParse(raw, out var n) ? n : 0;
                }

                // Confusion matrix is small enough to load fully into memory.
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT target_id, picked_id, count FROM confusions";
                    using var rd = cmd.ExecuteReader();
                    while (rd.Read())
                        _confusions[(rd.GetString(0), rd.GetString(1))] = rd.GetInt32(2);
                }

                // FSRS state for every word that has any review history. Cheap to keep in
                // memory since we need it on every pick under the FSRS strategy.
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT word_id, stability, difficulty, last_review_utc FROM fsrs_state";
                    using var rd = cmd.ExecuteReader();
                    while (rd.Read())
                    {
                        _fsrs[rd.GetString(0)] = new FsrsState
                        {
                            Stability = rd.GetDouble(1),
                            Difficulty = rd.GetDouble(2),
                            LastReviewUtc = DateTime.Parse(rd.GetString(3), null, System.Globalization.DateTimeStyles.RoundtripKind)
                        };
                    }
                }

                // Running response-time aggregates.
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT key, value FROM meta WHERE key IN ('time_count','time_sum','time_sumsq')";
                    using var rd = cmd.ExecuteReader();
                    while (rd.Read())
                    {
                        var k = rd.GetString(0);
                        var v = rd.GetString(1);
                        switch (k)
                        {
                            case "time_count": _timeCount = long.Parse(v); break;
                            case "time_sum":   _timeSum   = double.Parse(v, System.Globalization.CultureInfo.InvariantCulture); break;
                            case "time_sumsq": _timeSumSq = double.Parse(v, System.Globalization.CultureInfo.InvariantCulture); break;
                        }
                    }
                }
            }

            await TryImportLegacyJsonAsync();

            _loaded = true;
        }
        finally { _gate.Release(); }
    }

    /// <summary>One-time best-effort import from the old proficiency.json format.</summary>
    private async Task TryImportLegacyJsonAsync()
    {
        try
        {
            var dir = Path.GetDirectoryName(GetDbPath()) ?? "";
            var jsonPath = Path.Combine(dir, "proficiency.json");
            if (!File.Exists(jsonPath)) return;

            await using var fs = File.OpenRead(jsonPath);
            var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var list = await JsonSerializer.DeserializeAsync<List<WordProficiency>>(fs, opts) ?? new();
            await fs.DisposeAsync();

            using var conn = OpenConnection();
            using var tx = conn.BeginTransaction();
            foreach (var p in list)
                UpsertWordLocked(conn, tx, p);
            tx.Commit();

            // Rename the legacy file so we don't re-import on every launch.
            try { File.Move(jsonPath, jsonPath + ".bak", overwrite: true); }
            catch { /* keep going */ }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SqliteProficiencyStore] legacy-import failed: {ex.Message}");
        }
    }

    private static void EnsureSchema(SqliteConnection conn)
    {
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS proficiency_meta (
                    word_id           TEXT PRIMARY KEY,
                    last_seen_utc     TEXT,
                    total_seen        INTEGER NOT NULL DEFAULT 0,
                    total_correct     INTEGER NOT NULL DEFAULT 0,
                    next_due_at_turn  INTEGER NOT NULL DEFAULT 0,
                    is_reinforced     INTEGER NOT NULL DEFAULT 0
                );
                CREATE TABLE IF NOT EXISTS proficiency_scores (
                    word_id   TEXT NOT NULL,
                    criterion INTEGER NOT NULL,
                    score     REAL NOT NULL,
                    attempts  INTEGER NOT NULL,
                    PRIMARY KEY (word_id, criterion)
                );
                CREATE TABLE IF NOT EXISTS confusions (
                    target_id TEXT NOT NULL,
                    picked_id TEXT NOT NULL,
                    count     INTEGER NOT NULL,
                    PRIMARY KEY (target_id, picked_id)
                );
                CREATE INDEX IF NOT EXISTS confusions_target ON confusions(target_id);
                CREATE TABLE IF NOT EXISTS fsrs_state (
                    word_id         TEXT PRIMARY KEY,
                    stability       REAL NOT NULL,
                    difficulty      REAL NOT NULL,
                    last_review_utc TEXT NOT NULL
                );
                CREATE TABLE IF NOT EXISTS meta (
                    key   TEXT PRIMARY KEY,
                    value TEXT NOT NULL
                );";
            cmd.ExecuteNonQuery();
        }
    }

    public WordProficiency Get(string wordId)
    {
        if (_cache.TryGetValue(wordId, out var cached)) return cached;

        // Lazy load: a single-row + scores fetch when a word is touched for the first time.
        using var conn = OpenConnection();
        var p = LoadWordLocked(conn, wordId) ?? new WordProficiency { WordId = wordId };
        _cache[wordId] = p;
        return p;
    }

    public IEnumerable<WordProficiency> All()
    {
        // For analytics and the progress page; pulls every row. Cache is updated as a side
        // effect so subsequent Get() calls hit memory.
        using var conn = OpenConnection();
        EnsureSchema(conn);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT word_id FROM proficiency_meta";
        using var rd = cmd.ExecuteReader();
        var ids = new List<string>();
        while (rd.Read()) ids.Add(rd.GetString(0));
        foreach (var id in ids)
            yield return Get(id);
    }

    private static WordProficiency? LoadWordLocked(SqliteConnection conn, string wordId)
    {
        WordProficiency? p = null;
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT last_seen_utc, total_seen, total_correct, next_due_at_turn, is_reinforced FROM proficiency_meta WHERE word_id = $id";
            cmd.Parameters.AddWithValue("$id", wordId);
            using var rd = cmd.ExecuteReader();
            if (rd.Read())
            {
                p = new WordProficiency
                {
                    WordId = wordId,
                    LastSeenUtc = rd.IsDBNull(0) ? null : DateTime.Parse(rd.GetString(0), null, System.Globalization.DateTimeStyles.RoundtripKind),
                    TotalSeen = rd.GetInt32(1),
                    TotalCorrect = rd.GetInt32(2),
                    NextDueAtTurn = rd.GetInt32(3),
                    IsReinforced = rd.GetInt32(4) != 0
                };
            }
        }
        if (p is null) return null;

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT criterion, score, attempts FROM proficiency_scores WHERE word_id = $id";
            cmd.Parameters.AddWithValue("$id", wordId);
            using var rd = cmd.ExecuteReader();
            while (rd.Read())
            {
                var c = (ProficiencyCriterion)rd.GetInt32(0);
                p.Scores[c] = rd.GetDouble(1);
                p.Attempts[c] = rd.GetInt32(2);
            }
        }
        return p;
    }

    private static void UpsertWordLocked(SqliteConnection conn, SqliteTransaction? tx, WordProficiency p)
    {
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = @"
                INSERT INTO proficiency_meta (word_id, last_seen_utc, total_seen, total_correct, next_due_at_turn, is_reinforced)
                VALUES ($id, $last, $seen, $correct, $due, $rein)
                ON CONFLICT(word_id) DO UPDATE SET
                    last_seen_utc = excluded.last_seen_utc,
                    total_seen = excluded.total_seen,
                    total_correct = excluded.total_correct,
                    next_due_at_turn = excluded.next_due_at_turn,
                    is_reinforced = excluded.is_reinforced;";
            cmd.Parameters.AddWithValue("$id", p.WordId);
            cmd.Parameters.AddWithValue("$last", p.LastSeenUtc?.ToString("o") ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("$seen", p.TotalSeen);
            cmd.Parameters.AddWithValue("$correct", p.TotalCorrect);
            cmd.Parameters.AddWithValue("$due", p.NextDueAtTurn);
            cmd.Parameters.AddWithValue("$rein", p.IsReinforced ? 1 : 0);
            cmd.ExecuteNonQuery();
        }

        foreach (var (c, s) in p.Scores)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
                INSERT INTO proficiency_scores (word_id, criterion, score, attempts)
                VALUES ($id, $c, $s, $a)
                ON CONFLICT(word_id, criterion) DO UPDATE SET
                    score = excluded.score, attempts = excluded.attempts;";
            cmd.Parameters.AddWithValue("$id", p.WordId);
            cmd.Parameters.AddWithValue("$c", (int)c);
            cmd.Parameters.AddWithValue("$s", s);
            cmd.Parameters.AddWithValue("$a", p.Attempts.TryGetValue(c, out var a) ? a : 0);
            cmd.ExecuteNonQuery();
        }
    }

    public async Task SetReinforcedAsync(string wordId, bool reinforced)
    {
        var p = Get(wordId);
        if (p.IsReinforced == reinforced) return;
        p.IsReinforced = reinforced;
        if (reinforced) p.NextDueAtTurn = _turnsAsked;
        await PersistWordAsync(p);
    }

    public async Task RecordAsync(string wordId, ProficiencyCriterion criterion, bool correct, int elapsedMs = 0)
    {
        var p = Get(wordId);
        p.RecordResult(criterion, correct);
        _turnsAsked++;
        p.NextDueAtTurn = _turnsAsked + ComputeInterval(p.Overall, correct);

        // FSRS update — uses the elapsed time z-score to derive a 4-level grade.
        var now = DateTime.UtcNow;
        var grade = Fsrs.GradeFromTime(correct, ZScoreFromMs(elapsedMs));
        var prevState = _fsrs.TryGetValue(wordId, out var existing) ? existing : default;
        var newState = Fsrs.Update(prevState, grade, now);
        _fsrs[wordId] = newState;

        // Update response-time stats — only count correct answers; wrong answers correlate
        // with hesitation in a way that would skew the mean upward.
        if (correct && elapsedMs > 0)
        {
            _timeCount++;
            _timeSum   += elapsedMs;
            _timeSumSq += (double)elapsedMs * elapsedMs;
        }

        await _gate.WaitAsync();
        try
        {
            using var conn = OpenConnection();
            using var tx = conn.BeginTransaction();
            UpsertWordLocked(conn, tx, p);
            UpsertFsrsLocked(conn, tx, wordId, newState);
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = @"
                    INSERT INTO meta(key, value) VALUES($k, $v) ON CONFLICT(key) DO UPDATE SET value = excluded.value;";
                void SetMeta(string key, string value)
                {
                    cmd.Parameters.Clear();
                    cmd.Parameters.AddWithValue("$k", key);
                    cmd.Parameters.AddWithValue("$v", value);
                    cmd.ExecuteNonQuery();
                }
                SetMeta(TurnsMetaKey, _turnsAsked.ToString());
                SetMeta("time_count", _timeCount.ToString());
                SetMeta("time_sum",   _timeSum.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
                SetMeta("time_sumsq", _timeSumSq.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
            }
            tx.Commit();
        }
        finally { _gate.Release(); }
    }

    private double ZScoreFromMs(int elapsedMs)
    {
        // Need at least a small sample before z-scoring; otherwise treat as "Good".
        if (elapsedMs <= 0 || _timeCount < 8) return 0;
        var mean = _timeSum / _timeCount;
        var variance = (_timeSumSq / _timeCount) - mean * mean;
        var stddev = variance > 0 ? Math.Sqrt(variance) : 0;
        if (stddev < 1) return 0;
        return (elapsedMs - mean) / stddev;
    }

    private static void UpsertFsrsLocked(SqliteConnection conn, SqliteTransaction tx, string wordId, FsrsState s)
    {
        if (!s.HasState) return;
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
            INSERT INTO fsrs_state (word_id, stability, difficulty, last_review_utc)
            VALUES ($id, $s, $d, $t)
            ON CONFLICT(word_id) DO UPDATE SET
                stability = excluded.stability,
                difficulty = excluded.difficulty,
                last_review_utc = excluded.last_review_utc;";
        cmd.Parameters.AddWithValue("$id", wordId);
        cmd.Parameters.AddWithValue("$s", s.Stability);
        cmd.Parameters.AddWithValue("$d", s.Difficulty);
        cmd.Parameters.AddWithValue("$t", s.LastReviewUtc!.Value.ToString("o"));
        cmd.ExecuteNonQuery();
    }

    public FsrsState GetFsrsState(string wordId) =>
        _fsrs.TryGetValue(wordId, out var s) ? s : default;

    public IEnumerable<(string WordId, FsrsState State)> AllFsrsStates()
    {
        foreach (var (id, s) in _fsrs) yield return (id, s);
    }

    public Task SaveAsync() => Task.CompletedTask; // every mutation persists immediately

    private async Task PersistWordAsync(WordProficiency p)
    {
        await _gate.WaitAsync();
        try
        {
            using var conn = OpenConnection();
            using var tx = conn.BeginTransaction();
            UpsertWordLocked(conn, tx, p);
            tx.Commit();
        }
        finally { _gate.Release(); }
    }

    public async Task ResetAsync()
    {
        await _gate.WaitAsync();
        try
        {
            _cache.Clear();
            _confusions.Clear();
            _fsrs.Clear();
            _turnsAsked = 0;
            _timeCount = 0;
            _timeSum = 0;
            _timeSumSq = 0;

            using var conn = OpenConnection();
            using var tx = conn.BeginTransaction();
            foreach (var table in new[] { "proficiency_scores", "proficiency_meta", "confusions", "fsrs_state", "meta" })
            {
                using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = $"DELETE FROM {table}";
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
        }
        finally { _gate.Release(); }
    }

    public async Task RecordConfusionAsync(string targetId, string pickedId)
    {
        if (string.IsNullOrEmpty(targetId) || string.IsNullOrEmpty(pickedId)) return;
        var key = (targetId, pickedId);
        var newCount = _confusions[key] = _confusions.TryGetValue(key, out var c) ? c + 1 : 1;

        await _gate.WaitAsync();
        try
        {
            using var conn = OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO confusions (target_id, picked_id, count) VALUES ($t, $p, $c)
                ON CONFLICT(target_id, picked_id) DO UPDATE SET count = excluded.count;";
            cmd.Parameters.AddWithValue("$t", targetId);
            cmd.Parameters.AddWithValue("$p", pickedId);
            cmd.Parameters.AddWithValue("$c", newCount);
            cmd.ExecuteNonQuery();
        }
        finally { _gate.Release(); }
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

    /// <summary>Mirrors the original ProficiencyStore.ComputeInterval — kept identical.</summary>
    private static int ComputeInterval(double overall, bool correct)
    {
        if (!correct) return Random.Shared.Next(1, 3);
        var raw = 2.0 * Math.Pow(1.6, overall / 12.0);
        return (int)Math.Clamp(Math.Round(raw), 2, 250);
    }
}
