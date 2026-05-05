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
    /// Loads the committed fixture and asserts every persisted field comes back with the
    /// exact value it was written with. Any future change that breaks reading an existing
    /// user's database will fail this test.
    /// </summary>
    [Fact]
    public async Task GoldenFixture_LoadsWithExpectedValues()
    {
        var fixturePath = FixtureUtil.PathTo("proficiency-v1.db");
        Assert.True(File.Exists(fixturePath),
            $"Golden fixture missing at '{fixturePath}'. " +
            $"Run the {nameof(GenerateFixture)} test (set the env var LEARNJP_REGEN_FIXTURE=1) to recreate it, then commit the file.");

        // Copy to a temp file so the test never mutates the committed bytes.
        var workingCopy = TempDb();
        File.Copy(fixturePath, workingCopy, overwrite: true);
        try
        {
            var store = new SqliteProficiencyStore(workingCopy);
            await store.LoadAsync();

            // Pin the global turn counter — moving this would mean we silently lost user progress.
            Assert.Equal(FixtureSpec.TurnsAsked, store.TurnsAsked);

            // Every word documented in the spec must load with the exact stored values.
            foreach (var (wordId, expected) in FixtureSpec.Words)
            {
                var actual = Snapshot(store.Get(wordId));
                AssertSameProficiency(expected, actual);
            }

            // FSRS state for the words we exercised.
            foreach (var (wordId, expected) in FixtureSpec.FsrsStates)
            {
                var s = store.GetFsrsState(wordId);
                Assert.True(s.HasState, $"missing FSRS state for {wordId}");
                Assert.Equal(expected.Stability,     s.Stability,     8);
                Assert.Equal(expected.Difficulty,    s.Difficulty,    8);
                Assert.Equal(expected.LastReviewUtc, s.LastReviewUtc);
            }

            // Confusion matrix.
            foreach (var (target, expected) in FixtureSpec.ConfusersByTarget)
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
    /// Schema-shape assertion. Going beyond the data values: this verifies the exact set of
    /// tables and columns is unchanged. If someone renames a column or adds a NOT NULL
    /// without a default, this fires immediately rather than waiting for a load to fail
    /// somewhere subtle.
    /// </summary>
    [Fact]
    public void GoldenFixture_SchemaMatchesExpected()
    {
        var fixturePath = FixtureUtil.PathTo("proficiency-v1.db");
        Assert.True(File.Exists(fixturePath), "fixture missing — see GoldenFixture_LoadsWithExpectedValues for regeneration.");

        var workingCopy = TempDb();
        File.Copy(fixturePath, workingCopy, overwrite: true);
        try
        {
            var schema = ReadSchema(workingCopy);
            // Compare as a sorted multi-line block so the failure diff is readable.
            Assert.Equal(FixtureSpec.ExpectedSchemaSignature.Trim(), schema.Trim());
        }
        finally { TryDelete(workingCopy); }
    }

    // --- Fixture regeneration ---------------------------------------------

    /// <summary>
    /// Skipped by default — regenerates the committed fixture on disk. Set the env var
    /// <c>LEARNJP_REGEN_FIXTURE=1</c> and run this test to refresh the golden file after
    /// an intentional schema change. The output is written into the source tree (Fixtures\
    /// next to the .csproj), not the bin\ copy.
    ///
    /// We write rows via raw SQL instead of calling <see cref="SqliteProficiencyStore.RecordAsync"/>
    /// so the on-disk values exactly match <see cref="FixtureSpec"/> — RecordAsync uses
    /// <c>DateTime.UtcNow</c> and <c>Random.Shared</c>, which would make the fixture
    /// non-deterministic.
    /// </summary>
    [Fact]
    public async Task GenerateFixture()
    {
        if (Environment.GetEnvironmentVariable("LEARNJP_REGEN_FIXTURE") != "1") return;

        var sourcePath = FixtureUtil.SourceTreePath("proficiency-v1.db");
        Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
        TryDelete(sourcePath);

        // Use the production store solely to install the canonical schema. The store's
        // LoadAsync calls EnsureSchema once at startup; calling it on a fresh path is enough.
        var store = new SqliteProficiencyStore(sourcePath);
        await store.LoadAsync();

        await FixtureBuilder.WriteAsync(sourcePath);
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
