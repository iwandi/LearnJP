using LearnJP.Models;
using LearnJP.Services;

namespace LearnJP.Tests.SaveData;

/// <summary>
/// The exact, hand-pinned values stored in <c>Fixtures\proficiency-v1.db</c>. Kept as
/// constants (no wall-clock, no randomness) so the load test can assert on byte-perfect
/// equality. If you need to change the contents, update the spec, run
/// <c>SqliteProficiencyStoreTests.GenerateFixture</c> with <c>LEARNJP_REGEN_FIXTURE=1</c>,
/// and commit the regenerated <c>.db</c> alongside the spec edit.
/// </summary>
internal static class FixtureSpec
{
    public static readonly DateTime FixedNow =
        new(2024, 06, 01, 12, 00, 00, DateTimeKind.Utc);

    public const int TurnsAsked = 7;

    public static readonly Dictionary<string, SqliteProficiencyStoreTests.ProficiencySnapshot> Words =
        new()
        {
            ["fixture-1"] = new(
                WordId: "fixture-1",
                LastSeenUtc: FixedNow,
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
                LastSeenUtc: FixedNow,
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
        };

    public static readonly Dictionary<string, FsrsState> FsrsStates = new()
    {
        ["fixture-1"] = new FsrsState
        {
            Stability = 4.0,
            Difficulty = 5.0,
            LastReviewUtc = FixedNow,
        },
        ["fixture-2"] = new FsrsState
        {
            Stability = 8.0,
            Difficulty = 3.0,
            LastReviewUtc = FixedNow,
        },
    };

    public static readonly Dictionary<string, (string PickedId, int Count)[]> ConfusersByTarget =
        new()
        {
            ["fixture-1"] = new[]
            {
                ("fixture-2", 2),
                ("fixture-3", 1),
            },
        };

    /// <summary>
    /// Pinned schema signature (sqlite_schema rows, whitespace-collapsed, sorted by
    /// type/name). Any structural change — new column, renamed table, dropped index — will
    /// fail <c>GoldenFixture_SchemaMatchesExpected</c>. When changing the schema
    /// intentionally, regenerate the fixture and update this constant in the same commit.
    /// </summary>
    public const string ExpectedSchemaSignature = """
index confusions_target: CREATE INDEX confusions_target ON confusions(target_id)
table confusions: CREATE TABLE confusions ( target_id TEXT NOT NULL, picked_id TEXT NOT NULL, count INTEGER NOT NULL, PRIMARY KEY (target_id, picked_id) )
table fsrs_state: CREATE TABLE fsrs_state ( word_id TEXT PRIMARY KEY, stability REAL NOT NULL, difficulty REAL NOT NULL, last_review_utc TEXT NOT NULL )
table meta: CREATE TABLE meta ( key TEXT PRIMARY KEY, value TEXT NOT NULL )
table proficiency_meta: CREATE TABLE proficiency_meta ( word_id TEXT PRIMARY KEY, last_seen_utc TEXT, total_seen INTEGER NOT NULL DEFAULT 0, total_correct INTEGER NOT NULL DEFAULT 0, next_due_at_turn INTEGER NOT NULL DEFAULT 0, is_reinforced INTEGER NOT NULL DEFAULT 0 )
table proficiency_scores: CREATE TABLE proficiency_scores ( word_id TEXT NOT NULL, criterion INTEGER NOT NULL, score REAL NOT NULL, attempts INTEGER NOT NULL, PRIMARY KEY (word_id, criterion) )
""";
}
