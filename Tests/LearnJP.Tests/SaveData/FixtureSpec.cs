using LearnJP.Models;
using LearnJP.Services;

namespace LearnJP.Tests.SaveData;

/// <summary>
/// One frozen snapshot of the SQLite save format. The fields below describe the contents
/// of a single committed fixture file in <c>Fixtures/</c>; <see cref="GoldenFixtures.All"/>
/// holds a list of every committed snapshot that the current code is expected to read.
///
/// When a schema change ships:
/// <list type="number">
///   <item>Add a new <see cref="GoldenFixture"/> to the registry (e.g. <c>BuildV2()</c>).</item>
///   <item>Run <c>SqliteProficiencyStoreTests.GenerateFixture</c> with
///         <c>LEARNJP_REGEN_FIXTURE=1</c> to materialise it on disk.</item>
///   <item>Keep the older fixtures in place — they prove old saves still load.</item>
/// </list>
/// </summary>
internal sealed class GoldenFixture
{
    public required string FileName { get; init; }
    public required int TurnsAsked { get; init; }
    public required IReadOnlyDictionary<string, SqliteProficiencyStoreTests.ProficiencySnapshot> Words { get; init; }
    public required IReadOnlyDictionary<string, FsrsState> FsrsStates { get; init; }
    public required IReadOnlyDictionary<string, (string PickedId, int Count)[]> ConfusersByTarget { get; init; }
    public required string ExpectedSchemaSignature { get; init; }

    public override string ToString() => FileName;
}

/// <summary>Registry of every committed golden fixture the current code must still read.</summary>
internal static class GoldenFixtures
{
    // -- v1 ---------------------------------------------------------------
    //
    // NOTE: static field initialisation order is textual top-to-bottom. Per-version
    // constants must be declared BEFORE the registry, otherwise the registry's initialiser
    // runs while they're still `default(...)` — silent corruption of the spec.

    private static readonly DateTime V1FixedNow =
        new(2024, 06, 01, 12, 00, 00, DateTimeKind.Utc);

    public static readonly IReadOnlyList<GoldenFixture> All = new[]
    {
        BuildV1(),
    };

    public static GoldenFixture ByFileName(string name) =>
        All.First(f => f.FileName == name);

    private static GoldenFixture BuildV1() => new()
    {
        FileName   = "proficiency-v1.db",
        TurnsAsked = 7,
        Words = new Dictionary<string, SqliteProficiencyStoreTests.ProficiencySnapshot>
        {
            ["fixture-1"] = new(
                WordId: "fixture-1",
                LastSeenUtc: V1FixedNow,
                TotalSeen: 4,
                TotalCorrect: 3,
                NextDueAtTurn: 9,
                IsReinforced: false,
                Scores: new()
                {
                    [ProficiencyCriterion.TargetToBase]                  = 75.0,
                    [ProficiencyCriterion.BaseToTarget]                  = 30.0,
                    [ProficiencyCriterion.SimilarSoundDifferentiation]   = 60.0,
                    [ProficiencyCriterion.SimilarMeaningDifferentiation] = 12.5,
                },
                Attempts: new()
                {
                    [ProficiencyCriterion.TargetToBase]                  = 2,
                    [ProficiencyCriterion.BaseToTarget]                  = 1,
                    [ProficiencyCriterion.SimilarSoundDifferentiation]   = 1,
                    [ProficiencyCriterion.SimilarMeaningDifferentiation] = 0,
                }),
            ["fixture-2"] = new(
                WordId: "fixture-2",
                LastSeenUtc: V1FixedNow,
                TotalSeen: 3,
                TotalCorrect: 3,
                NextDueAtTurn: 12,
                IsReinforced: true,
                Scores: new()
                {
                    [ProficiencyCriterion.TargetToBase]                  = 90.0,
                    [ProficiencyCriterion.BaseToTarget]                  = 0.0,
                    [ProficiencyCriterion.SimilarSoundDifferentiation]   = 50.0,
                    [ProficiencyCriterion.SimilarMeaningDifferentiation] = 0.0,
                },
                Attempts: new()
                {
                    [ProficiencyCriterion.TargetToBase]                  = 2,
                    [ProficiencyCriterion.BaseToTarget]                  = 0,
                    [ProficiencyCriterion.SimilarSoundDifferentiation]   = 1,
                    [ProficiencyCriterion.SimilarMeaningDifferentiation] = 0,
                }),
        },
        FsrsStates = new Dictionary<string, FsrsState>
        {
            ["fixture-1"] = new FsrsState { Stability = 4.0, Difficulty = 5.0, LastReviewUtc = V1FixedNow },
            ["fixture-2"] = new FsrsState { Stability = 8.0, Difficulty = 3.0, LastReviewUtc = V1FixedNow },
        },
        ConfusersByTarget = new Dictionary<string, (string, int)[]>
        {
            ["fixture-1"] = new[]
            {
                ("fixture-2", 2),
                ("fixture-3", 1),
            },
        },
        ExpectedSchemaSignature = """
index confusions_target: CREATE INDEX confusions_target ON confusions(target_id)
table confusions: CREATE TABLE confusions ( target_id TEXT NOT NULL, picked_id TEXT NOT NULL, count INTEGER NOT NULL, PRIMARY KEY (target_id, picked_id) )
table fsrs_state: CREATE TABLE fsrs_state ( word_id TEXT PRIMARY KEY, stability REAL NOT NULL, difficulty REAL NOT NULL, last_review_utc TEXT NOT NULL )
table meta: CREATE TABLE meta ( key TEXT PRIMARY KEY, value TEXT NOT NULL )
table proficiency_meta: CREATE TABLE proficiency_meta ( word_id TEXT PRIMARY KEY, last_seen_utc TEXT, total_seen INTEGER NOT NULL DEFAULT 0, total_correct INTEGER NOT NULL DEFAULT 0, next_due_at_turn INTEGER NOT NULL DEFAULT 0, is_reinforced INTEGER NOT NULL DEFAULT 0 )
table proficiency_scores: CREATE TABLE proficiency_scores ( word_id TEXT NOT NULL, criterion INTEGER NOT NULL, score REAL NOT NULL, attempts INTEGER NOT NULL, PRIMARY KEY (word_id, criterion) )
""",
    };
}
