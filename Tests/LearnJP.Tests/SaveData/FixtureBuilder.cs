using LearnJP.Models;
using Microsoft.Data.Sqlite;

namespace LearnJP.Tests.SaveData;

/// <summary>
/// Writes the exact contents of a <see cref="GoldenFixture"/> into a SQLite database the
/// production store can later open. Build steps are intentionally low-level (raw INSERTs
/// against the schema declared in <see cref="LearnJP.Services.SqliteProficiencyStore"/>)
/// so the result is bit-for-bit deterministic — call sites can assert on equality
/// without rounding around wall-clock time or random jitter.
/// </summary>
internal static class FixtureBuilder
{
    public static Task WriteAsync(string dbPath, GoldenFixture spec)
    {
        using var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();
        using var tx = conn.BeginTransaction();

        // proficiency_meta + proficiency_scores
        foreach (var (id, snap) in spec.Words)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = @"
                    INSERT INTO proficiency_meta (word_id, last_seen_utc, total_seen, total_correct, next_due_at_turn, is_reinforced)
                    VALUES ($id, $last, $seen, $correct, $due, $rein);";
                cmd.Parameters.AddWithValue("$id", id);
                cmd.Parameters.AddWithValue("$last", snap.LastSeenUtc?.ToString("o") ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("$seen", snap.TotalSeen);
                cmd.Parameters.AddWithValue("$correct", snap.TotalCorrect);
                cmd.Parameters.AddWithValue("$due", snap.NextDueAtTurn);
                cmd.Parameters.AddWithValue("$rein", snap.IsReinforced ? 1 : 0);
                cmd.ExecuteNonQuery();
            }

            foreach (var c in ProficiencyCriterionExtensions.All)
            {
                using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = @"
                    INSERT INTO proficiency_scores (word_id, criterion, score, attempts)
                    VALUES ($id, $c, $s, $a);";
                cmd.Parameters.AddWithValue("$id", id);
                cmd.Parameters.AddWithValue("$c", (int)c);
                cmd.Parameters.AddWithValue("$s", snap.Scores[c]);
                cmd.Parameters.AddWithValue("$a", snap.Attempts[c]);
                cmd.ExecuteNonQuery();
            }
        }

        // confusions
        foreach (var (target, picks) in spec.ConfusersByTarget)
        foreach (var (picked, count) in picks)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
                INSERT INTO confusions (target_id, picked_id, count) VALUES ($t, $p, $c);";
            cmd.Parameters.AddWithValue("$t", target);
            cmd.Parameters.AddWithValue("$p", picked);
            cmd.Parameters.AddWithValue("$c", count);
            cmd.ExecuteNonQuery();
        }

        // fsrs_state
        foreach (var (id, state) in spec.FsrsStates)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
                INSERT INTO fsrs_state (word_id, stability, difficulty, last_review_utc)
                VALUES ($id, $s, $d, $t);";
            cmd.Parameters.AddWithValue("$id", id);
            cmd.Parameters.AddWithValue("$s", state.Stability);
            cmd.Parameters.AddWithValue("$d", state.Difficulty);
            cmd.Parameters.AddWithValue("$t", state.LastReviewUtc!.Value.ToString("o"));
            cmd.ExecuteNonQuery();
        }

        // meta — turns_asked + time aggregates (kept hard-coded since they aren't part of the
        // per-fixture spec; if a future schema breaks here, regenerate the fixture).
        void SetMeta(string key, string value)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
                INSERT INTO meta(key, value) VALUES($k, $v)
                ON CONFLICT(key) DO UPDATE SET value = excluded.value;";
            cmd.Parameters.AddWithValue("$k", key);
            cmd.Parameters.AddWithValue("$v", value);
            cmd.ExecuteNonQuery();
        }
        SetMeta("turns_asked", spec.TurnsAsked.ToString());
        SetMeta("time_count",  "12");
        SetMeta("time_sum",    "14400");
        SetMeta("time_sumsq",  "21600000");

        tx.Commit();
        return Task.CompletedTask;
    }
}
