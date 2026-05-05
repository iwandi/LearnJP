using LearnJP.Models;
using LearnJP.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace LearnJP.Tests.SaveData;

/// <summary>
/// Save-data integrity tests for <see cref="SqliteProficiencyStore"/>.
///
/// Two layers, each catching a different class of regression:
///
/// <list type="bullet">
///   <item><b>Roundtrip</b> — populate every persisted field, recreate the store on the same
///         file, and assert deep equality. Catches symmetric write/read bugs.</item>
///   <item><b>Golden fixture</b> — open a committed pre-built database and assert each row
///         loads with the exact expected values. Catches schema/format/enum-value drift
///         that would silently corrupt existing user data on upgrade.</item>
/// </list>
///
/// When a schema change is intentional, regenerate the fixture by running the
/// <see cref="GenerateFixture"/> helper test (it is normally skipped) and commit the
/// resulting file under Fixtures\.
/// </summary>
public sealed class SqliteProficiencyStoreTests
{
    // --- Roundtrip ---------------------------------------------------------

    [Fact]
    public async Task Roundtrip_PreservesAllPersistedFields()
    {
        var path = TempDb();
        try
        {
            // Write a richly-populated state.
            var store = new SqliteProficiencyStore(path);
            await store.LoadAsync();

            await store.RecordAsync("w-alpha", ProficiencyCriterion.TargetToBase, correct: true,  elapsedMs: 1200);
            await store.RecordAsync("w-alpha", ProficiencyCriterion.BaseToTarget, correct: false, elapsedMs: 4200);
            await store.RecordAsync("w-beta",  ProficiencyCriterion.SimilarSoundDifferentiation, correct: true, elapsedMs: 800);
            await store.RecordAsync("w-beta",  ProficiencyCriterion.SimilarMeaningDifferentiation, correct: true, elapsedMs: 950);
            await store.SetReinforcedAsync("w-beta", reinforced: true);
            await store.RecordConfusionAsync(targetId: "w-alpha", pickedId: "w-beta");
            await store.RecordConfusionAsync(targetId: "w-alpha", pickedId: "w-beta");
            await store.RecordConfusionAsync(targetId: "w-alpha", pickedId: "w-gamma");
            await store.AdjustScoresAsync("w-gamma", +12.5);

            var turnsAfterWrite     = store.TurnsAsked;
            var alphaAfterWrite     = Snapshot(store.Get("w-alpha"));
            var betaAfterWrite      = Snapshot(store.Get("w-beta"));
            var gammaAfterWrite     = Snapshot(store.Get("w-gamma"));
            var betaFsrsAfterWrite  = store.GetFsrsState("w-beta");

            // Reload in a brand-new instance — the only way to verify the on-disk state
            // and not just the in-memory cache.
            var reloaded = new SqliteProficiencyStore(path);
            await reloaded.LoadAsync();

            Assert.Equal(turnsAfterWrite, reloaded.TurnsAsked);

            AssertSameProficiency(alphaAfterWrite, Snapshot(reloaded.Get("w-alpha")));
            AssertSameProficiency(betaAfterWrite,  Snapshot(reloaded.Get("w-beta")));
            AssertSameProficiency(gammaAfterWrite, Snapshot(reloaded.Get("w-gamma")));

            var betaFsrsReloaded = reloaded.GetFsrsState("w-beta");
            Assert.Equal(betaFsrsAfterWrite.Stability,    betaFsrsReloaded.Stability,    8);
            Assert.Equal(betaFsrsAfterWrite.Difficulty,   betaFsrsReloaded.Difficulty,   8);
            Assert.Equal(betaFsrsAfterWrite.LastReviewUtc, betaFsrsReloaded.LastReviewUtc);

            // Confusions roundtrip with their counts intact and ordered descending.
            var confusers = reloaded.GetTopConfusersFor("w-alpha", limit: 5);
            Assert.Equal(2, confusers.Count);
            Assert.Equal("w-beta",  confusers[0].PickedId);
            Assert.Equal(2,         confusers[0].Count);
            Assert.Equal("w-gamma", confusers[1].PickedId);
            Assert.Equal(1,         confusers[1].Count);

            Assert.Contains("w-alpha", reloaded.GetConfusedTargetIds());

            // All() walks the meta table and pulls every word back via Get(), so the count
            // should match what we wrote.
            var ids = reloaded.All().Select(p => p.WordId).OrderBy(s => s).ToArray();
            Assert.Equal(new[] { "w-alpha", "w-beta", "w-gamma" }, ids);
        }
        finally { TryDelete(path); }
    }

    /// <summary>
    /// Pins the *exact bytes* the production write path puts on disk. The roundtrip test
    /// only proves write+read are symmetric — both could change in lockstep and tests
    /// would still pass. This test asserts column types and value formats directly via
    /// raw SQL so a unilateral change to the write side fails immediately.
    /// </summary>
    [Fact]
    public async Task ProductionWritePath_EmitsExpectedOnDiskShape()
    {
        var path = TempDb();
        try
        {
            var store = new SqliteProficiencyStore(path);
            await store.LoadAsync();
            await store.RecordAsync("shape-1", ProficiencyCriterion.BaseToTarget, correct: true, elapsedMs: 1000);
            await store.RecordAsync("shape-1", ProficiencyCriterion.SimilarMeaningDifferentiation, correct: false, elapsedMs: 4000);
            await store.SetReinforcedAsync("shape-1", reinforced: true);
            await store.RecordConfusionAsync("shape-1", "shape-2");

            using var conn = new SqliteConnection($"Data Source={path}");
            conn.Open();

            // proficiency_meta — last_seen_utc must be ISO-8601 round-trippable ("o" format).
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT last_seen_utc, total_seen, total_correct, is_reinforced FROM proficiency_meta WHERE word_id = 'shape-1'";
                using var rd = cmd.ExecuteReader();
                Assert.True(rd.Read());

                var lastSeen = rd.GetString(0);
                // The "o" format yields strings like "2024-06-01T12:00:00.0000000Z" — the
                // important guarantees are: parseable round-trippable, ends with 'Z' (UTC),
                // contains a 'T' separator. If anyone changes ToString("o") to "u" or local
                // time, this fires.
                Assert.EndsWith("Z", lastSeen);
                Assert.Contains("T", lastSeen);
                var parsed = DateTime.Parse(lastSeen, null, System.Globalization.DateTimeStyles.RoundtripKind);
                Assert.Equal(DateTimeKind.Utc, parsed.Kind);

                Assert.Equal(2, rd.GetInt32(1));
                Assert.Equal(1, rd.GetInt32(2));
                Assert.Equal(1, rd.GetInt32(3));   // is_reinforced is stored as 0/1 INTEGER
            }

            // proficiency_scores — criterion is stored as INTEGER (the enum int), score as REAL.
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT criterion, score, attempts FROM proficiency_scores WHERE word_id = 'shape-1' ORDER BY criterion";
                using var rd = cmd.ExecuteReader();
                var seen = new HashSet<int>();
                while (rd.Read())
                {
                    Assert.IsType<long>(rd.GetValue(0));   // SQLite reports INTEGER as Int64
                    Assert.IsType<double>(rd.GetValue(1));
                    seen.Add(rd.GetInt32(0));
                }
                Assert.Contains((int)ProficiencyCriterion.BaseToTarget, seen);
                Assert.Contains((int)ProficiencyCriterion.SimilarMeaningDifferentiation, seen);
            }

            // confusions — counts stored as INTEGER, no other columns.
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT target_id, picked_id, count FROM confusions";
                using var rd = cmd.ExecuteReader();
                Assert.True(rd.Read());
                Assert.Equal("shape-1", rd.GetString(0));
                Assert.Equal("shape-2", rd.GetString(1));
                Assert.Equal(1, rd.GetInt32(2));
                Assert.False(rd.Read());
            }

            // fsrs_state — last_review_utc must use the same ISO-8601 round-trip format.
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT stability, difficulty, last_review_utc FROM fsrs_state WHERE word_id = 'shape-1'";
                using var rd = cmd.ExecuteReader();
                Assert.True(rd.Read());
                Assert.IsType<double>(rd.GetValue(0));
                Assert.IsType<double>(rd.GetValue(1));
                var lastReview = rd.GetString(2);
                Assert.EndsWith("Z", lastReview);
                DateTime.Parse(lastReview, null, System.Globalization.DateTimeStyles.RoundtripKind);
            }

            // meta — turns_asked, time_count are integers stored as strings; time_sum / time_sumsq
            // must be invariant-culture decimal strings (so "," locales don't corrupt them).
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT key, value FROM meta WHERE key IN ('turns_asked','time_count','time_sum','time_sumsq')";
                using var rd = cmd.ExecuteReader();
                var meta = new Dictionary<string, string>();
                while (rd.Read()) meta[rd.GetString(0)] = rd.GetString(1);

                Assert.Equal(4, meta.Count);
                Assert.True(int.TryParse(meta["turns_asked"], out _));
                Assert.True(long.TryParse(meta["time_count"], out _));
                // Invariant culture parsing must succeed — i.e., decimal point is '.', no thousand sep.
                Assert.True(double.TryParse(meta["time_sum"],   System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _));
                Assert.True(double.TryParse(meta["time_sumsq"], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _));
                // Negative pin: ensure no comma decimal separator slipped in.
                Assert.DoesNotContain(",", meta["time_sum"]);
                Assert.DoesNotContain(",", meta["time_sumsq"]);
            }
        }
        finally { TryDelete(path); }
    }

    /// <summary>
    /// The response-time aggregates (<c>time_count</c>, <c>time_sum</c>, <c>time_sumsq</c>)
    /// are private fields with no public accessor, but they are part of the persisted save
    /// data — the FSRS grade-from-time mapping reads them on every answer. Reads them
    /// directly out of the meta table, then verifies they update with each correct answer
    /// and survive a reload.
    /// </summary>
    [Fact]
    public async Task TimeAggregates_PersistAndUpdateCorrectly()
    {
        var path = TempDb();
        try
        {
            var store = new SqliteProficiencyStore(path);
            await store.LoadAsync();

            // Wrong answers MUST NOT count — RecordAsync intentionally excludes them so
            // hesitation latency doesn't skew the mean. Pin that.
            await store.RecordAsync("a", ProficiencyCriterion.TargetToBase, correct: false, elapsedMs: 9999);
            AssertMeta(path, "time_count", "0");

            // 0/negative ms must also not count — represents a missing-data signal.
            await store.RecordAsync("a", ProficiencyCriterion.TargetToBase, correct: true, elapsedMs: 0);
            AssertMeta(path, "time_count", "0");

            // Two correct, timed answers — count, sum, sumsq must all advance.
            await store.RecordAsync("a", ProficiencyCriterion.TargetToBase, correct: true, elapsedMs: 1000);
            await store.RecordAsync("a", ProficiencyCriterion.TargetToBase, correct: true, elapsedMs: 2000);

            AssertMeta(path, "time_count", "2");
            AssertMetaDouble(path, "time_sum",   3000.0);
            AssertMetaDouble(path, "time_sumsq", 1000.0 * 1000.0 + 2000.0 * 2000.0);

            // Reload and check that the next correct answer continues the sequence — i.e.
            // the values were re-hydrated from disk, not reset to zero.
            var reloaded = new SqliteProficiencyStore(path);
            await reloaded.LoadAsync();
            await reloaded.RecordAsync("a", ProficiencyCriterion.TargetToBase, correct: true, elapsedMs: 3000);

            AssertMeta(path, "time_count", "3");
            AssertMetaDouble(path, "time_sum",   6000.0);
            AssertMetaDouble(path, "time_sumsq", 1000.0 * 1000.0 + 2000.0 * 2000.0 + 3000.0 * 3000.0);
        }
        finally { TryDelete(path); }
    }

    /// <summary>
    /// Behavioural test of the time-aggregate → z-score → FSRS-grade pipeline. Seeds enough
    /// correct answers at varied times to make z-scoring kick in, then probes a brand-new
    /// word with a fast and a slow answer respectively. The first-review FSRS state is a
    /// pure function of the grade, so we can assert <c>Stability</c> directly:
    /// <c>Easy → 8.0</c>, <c>Good → 4.0</c>, <c>Hard → 2.0</c>.
    /// </summary>
    [Theory]
    [InlineData(  100, 8.0, 3.0)]   // far below mean → Easy seed (Stability=8, Difficulty=3)
    [InlineData( 1700, 4.0, 5.0)]   // at mean       → Good seed (Stability=4, Difficulty=5)
    [InlineData( 6000, 2.0, 6.5)]   // far above mean → Hard seed (Stability=2, Difficulty=6.5)
    public async Task ZScore_DrivesFsrsGradeOnFirstReview(int elapsedMs, double expectedStability, double expectedDifficulty)
    {
        var path = TempDb();
        try
        {
            var store = new SqliteProficiencyStore(path);
            await store.LoadAsync();

            // Seed eight correct answers spread between 1000-2400ms — gives mean ≈ 1700,
            // stddev ≈ 458. ZScoreFromMs requires ≥ 8 samples before it activates.
            int[] seedMs = [1000, 1200, 1400, 1600, 1800, 2000, 2200, 2400];
            for (var i = 0; i < seedMs.Length; i++)
                await store.RecordAsync($"seed-{i}", ProficiencyCriterion.TargetToBase, correct: true, elapsedMs: seedMs[i]);

            // First review for an unseen word — Fsrs.Update takes the "seed S/D from grade"
            // branch, so the resulting state is exactly determined by the grade.
            await store.RecordAsync("probe", ProficiencyCriterion.TargetToBase, correct: true, elapsedMs: elapsedMs);

            var s = store.GetFsrsState("probe");
            Assert.True(s.HasState);
            Assert.Equal(expectedStability,  s.Stability,  6);
            Assert.Equal(expectedDifficulty, s.Difficulty, 6);
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public async Task ResetAsync_ClearsEverything()
    {
        var path = TempDb();
        try
        {
            var store = new SqliteProficiencyStore(path);
            await store.LoadAsync();
            await store.RecordAsync("x", ProficiencyCriterion.TargetToBase, correct: true, elapsedMs: 1000);
            await store.RecordConfusionAsync("x", "y");
            await store.SetReinforcedAsync("x", true);

            await store.ResetAsync();

            Assert.Equal(0, store.TurnsAsked);
            Assert.Empty(store.All());
            Assert.Empty(store.GetTopConfusersFor("x", 5));
            Assert.False(store.GetFsrsState("x").HasState);

            // Surviving the reload too — not just the in-memory caches.
            var reloaded = new SqliteProficiencyStore(path);
            await reloaded.LoadAsync();
            Assert.Equal(0, reloaded.TurnsAsked);
            Assert.Empty(reloaded.All());
        }
        finally { TryDelete(path); }
    }

    // --- Golden fixture ----------------------------------------------------

    /// <summary>
    /// Loads every committed fixture (one per past schema version) and asserts the current
    /// code can still read it field-for-field. As schemas evolve, older fixtures must keep
    /// loading — that's the whole point of keeping them around.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllFixtureNames))]
    public async Task GoldenFixture_LoadsWithExpectedValues(string fixtureFileName)
    {
        var spec = GoldenFixtures.ByFileName(fixtureFileName);
        var fixturePath = FixtureUtil.PathTo(spec.FileName);
        Assert.True(File.Exists(fixturePath),
            $"Golden fixture '{spec.FileName}' missing at '{fixturePath}'. " +
            $"Run the {nameof(GenerateFixture)} test with LEARNJP_REGEN_FIXTURE=1 to recreate it, then commit the file.");

        // Copy to a temp file so the test never mutates the committed bytes.
        var workingCopy = TempDb();
        File.Copy(fixturePath, workingCopy, overwrite: true);
        try
        {
            var store = new SqliteProficiencyStore(workingCopy);
            await store.LoadAsync();

            // Pin the global turn counter — moving this would mean we silently lost user progress.
            Assert.Equal(spec.TurnsAsked, store.TurnsAsked);

            foreach (var (wordId, expected) in spec.Words)
            {
                var actual = Snapshot(store.Get(wordId));
                AssertSameProficiency(expected, actual);
            }

            foreach (var (wordId, expected) in spec.FsrsStates)
            {
                var s = store.GetFsrsState(wordId);
                Assert.True(s.HasState, $"missing FSRS state for {wordId}");
                Assert.Equal(expected.Stability,     s.Stability,     8);
                Assert.Equal(expected.Difficulty,    s.Difficulty,    8);
                Assert.Equal(expected.LastReviewUtc, s.LastReviewUtc);
            }

            foreach (var (target, expected) in spec.ConfusersByTarget)
            {
                var actual = store.GetTopConfusersFor(target, limit: 10);
                Assert.Equal(expected.Length, actual.Count);
                for (var i = 0; i < expected.Length; i++)
                {
                    Assert.Equal(expected[i].PickedId, actual[i].PickedId);
                    Assert.Equal(expected[i].Count,    actual[i].Count);
                }
            }
        }
        finally { TryDelete(workingCopy); }
    }

    /// <summary>
    /// Schema-shape assertion per fixture. Verifies the exact set of tables and columns is
    /// unchanged for that snapshot. A column rename or a NOT NULL addition without a
    /// default fires here immediately rather than waiting for a subtle load failure.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllFixtureNames))]
    public void GoldenFixture_SchemaMatchesExpected(string fixtureFileName)
    {
        var spec = GoldenFixtures.ByFileName(fixtureFileName);
        var fixturePath = FixtureUtil.PathTo(spec.FileName);
        Assert.True(File.Exists(fixturePath), $"fixture '{spec.FileName}' missing — see GoldenFixture_LoadsWithExpectedValues for regeneration.");

        var workingCopy = TempDb();
        File.Copy(fixturePath, workingCopy, overwrite: true);
        try
        {
            var schema = ReadSchema(workingCopy);
            Assert.Equal(spec.ExpectedSchemaSignature.Trim(), schema.Trim());
        }
        finally { TryDelete(workingCopy); }
    }

    /// <summary>
    /// Idempotency: loading a populated database twice in sequence — second load on the
    /// same file — must not lose data. <see cref="SqliteProficiencyStore.LoadAsync"/> calls
    /// <c>EnsureSchema</c> with <c>CREATE TABLE IF NOT EXISTS</c>; this test pins that the
    /// "IF NOT EXISTS" clauses really do guard, and that no future migration accidentally
    /// truncates rows on second load.
    /// </summary>
    [Fact]
    public async Task LoadAsync_IsIdempotent_OnAPopulatedDatabase()
    {
        var path = TempDb();
        try
        {
            var first = new SqliteProficiencyStore(path);
            await first.LoadAsync();
            await first.RecordAsync("idem-1", ProficiencyCriterion.TargetToBase, correct: true,  elapsedMs: 1500);
            await first.RecordAsync("idem-2", ProficiencyCriterion.BaseToTarget, correct: false, elapsedMs: 4200);
            await first.RecordConfusionAsync("idem-1", "idem-2");
            await first.SetReinforcedAsync("idem-1", true);

            var snapshot1 = Snapshot(first.Get("idem-1"));
            var snapshot2 = Snapshot(first.Get("idem-2"));
            var fsrs1     = first.GetFsrsState("idem-1");
            var turns     = first.TurnsAsked;

            // Second store on the same file: triggers EnsureSchema again; verify nothing changed.
            var second = new SqliteProficiencyStore(path);
            await second.LoadAsync();

            Assert.Equal(turns, second.TurnsAsked);
            AssertSameProficiency(snapshot1, Snapshot(second.Get("idem-1")));
            AssertSameProficiency(snapshot2, Snapshot(second.Get("idem-2")));

            var fsrs1Reload = second.GetFsrsState("idem-1");
            Assert.Equal(fsrs1.Stability,     fsrs1Reload.Stability,     8);
            Assert.Equal(fsrs1.Difficulty,    fsrs1Reload.Difficulty,    8);
            Assert.Equal(fsrs1.LastReviewUtc, fsrs1Reload.LastReviewUtc);

            Assert.Single(second.GetTopConfusersFor("idem-1", 5));
        }
        finally { TryDelete(path); }
    }

    public static IEnumerable<object[]> AllFixtureNames() =>
        GoldenFixtures.All.Select(f => new object[] { f.FileName });

    // --- Fixture regeneration ---------------------------------------------

    /// <summary>
    /// Skipped by default — regenerates every committed fixture on disk. Set the env var
    /// <c>LEARNJP_REGEN_FIXTURE=1</c> and run this test to refresh the golden files after
    /// an intentional schema change. Output is written into the source tree (Fixtures\
    /// next to the .csproj), not the bin\ copy.
    ///
    /// Rows are written via raw SQL rather than <see cref="SqliteProficiencyStore.RecordAsync"/>
    /// so the on-disk values exactly match each <see cref="GoldenFixture"/> spec —
    /// <c>RecordAsync</c> uses <c>DateTime.UtcNow</c> and <c>Random.Shared</c>, which would
    /// make the fixture non-deterministic.
    /// </summary>
    [Fact]
    public async Task GenerateFixture()
    {
        if (Environment.GetEnvironmentVariable("LEARNJP_REGEN_FIXTURE") != "1") return;

        foreach (var spec in GoldenFixtures.All)
        {
            var sourcePath = FixtureUtil.SourceTreePath(spec.FileName);
            Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
            TryDelete(sourcePath);

            // Production store → installs the canonical schema. EnsureSchema runs at LoadAsync.
            var store = new SqliteProficiencyStore(sourcePath);
            await store.LoadAsync();

            await FixtureBuilder.WriteAsync(sourcePath, spec);
        }
    }

    // --- Helpers ----------------------------------------------------------

    private static string TempDb() =>
        Path.Combine(Path.GetTempPath(), $"learnjp-tests-{Guid.NewGuid():N}.db");

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* test cleanup is best-effort */ }
    }

    /// <summary>
    /// Captures every persisted field of a <see cref="WordProficiency"/> as plain values so
    /// equality comparisons don't drift if the model gets a new helper.
    /// </summary>
    private static ProficiencySnapshot Snapshot(WordProficiency p) => new(
        p.WordId,
        p.LastSeenUtc,
        p.TotalSeen,
        p.TotalCorrect,
        p.NextDueAtTurn,
        p.IsReinforced,
        ProficiencyCriterionExtensions.All.ToDictionary(c => c, p.GetScore),
        ProficiencyCriterionExtensions.All.ToDictionary(c => c, p.GetAttempts));

    internal sealed record ProficiencySnapshot(
        string WordId,
        DateTime? LastSeenUtc,
        int TotalSeen,
        int TotalCorrect,
        int NextDueAtTurn,
        bool IsReinforced,
        Dictionary<ProficiencyCriterion, double> Scores,
        Dictionary<ProficiencyCriterion, int> Attempts);

    private static void AssertSameProficiency(ProficiencySnapshot expected, ProficiencySnapshot actual)
    {
        Assert.Equal(expected.WordId,        actual.WordId);
        Assert.Equal(expected.LastSeenUtc,   actual.LastSeenUtc);
        Assert.Equal(expected.TotalSeen,     actual.TotalSeen);
        Assert.Equal(expected.TotalCorrect,  actual.TotalCorrect);
        Assert.Equal(expected.NextDueAtTurn, actual.NextDueAtTurn);
        Assert.Equal(expected.IsReinforced,  actual.IsReinforced);
        foreach (var c in ProficiencyCriterionExtensions.All)
        {
            Assert.Equal(expected.Scores[c],   actual.Scores[c],   8);
            Assert.Equal(expected.Attempts[c], actual.Attempts[c]);
        }
    }

    private static string? ReadMeta(string path, string key)
    {
        using var conn = new SqliteConnection($"Data Source={path}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT value FROM meta WHERE key = $k";
        cmd.Parameters.AddWithValue("$k", key);
        return cmd.ExecuteScalar() as string;
    }

    private static void AssertMeta(string path, string key, string expected) =>
        Assert.Equal(expected, ReadMeta(path, key));

    private static void AssertMetaDouble(string path, string key, double expected)
    {
        var raw = ReadMeta(path, key);
        Assert.NotNull(raw);
        var actual = double.Parse(raw!, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(expected, actual, 6);
    }

    private static string ReadSchema(string path)
    {
        using var conn = new SqliteConnection($"Data Source={path}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT type || ' ' || name || ': ' || COALESCE(sql, '<auto>') AS line
            FROM sqlite_schema
            WHERE name NOT LIKE 'sqlite_%'
            ORDER BY type, name;";
        using var rd = cmd.ExecuteReader();
        var lines = new List<string>();
        while (rd.Read())
        {
            // Normalise whitespace so trivial reformatting in EnsureSchema doesn't break the test.
            var raw = rd.GetString(0);
            var collapsed = System.Text.RegularExpressions.Regex.Replace(raw, @"\s+", " ").Trim();
            lines.Add(collapsed);
        }
        return string.Join("\n", lines);
    }
}
