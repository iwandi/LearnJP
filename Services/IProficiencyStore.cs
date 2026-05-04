using LearnJP.Models;

namespace LearnJP.Services;

public interface IProficiencyStore
{
    Task LoadAsync();
    Task SaveAsync();
    WordProficiency Get(string wordId);
    IEnumerable<WordProficiency> All();
    /// <summary>Monotonic counter incremented for every recorded answer.</summary>
    int TurnsAsked { get; }
    /// <summary>
    /// Records a review. <paramref name="elapsedMs"/> is the time the user spent on the
    /// question (click — start). 0 / negative = no-data, treated as "Good" by FSRS.
    /// </summary>
    Task RecordAsync(string wordId, ProficiencyCriterion criterion, bool correct, int elapsedMs = 0);

    /// <summary>FSRS state for this word; returns the empty state if the word hasn't been reviewed.</summary>
    FsrsState GetFsrsState(string wordId);

    /// <summary>Per-word FSRS state for every word that has any review history.</summary>
    IEnumerable<(string WordId, FsrsState State)> AllFsrsStates();
    Task SetReinforcedAsync(string wordId, bool reinforced);
    Task ResetAsync();

    /// <summary>
    /// Records that the user picked <paramref name="pickedId"/> when the correct answer was
    /// <paramref name="targetId"/>. Backs the confusion matrix used for distractor biasing
    /// and (later) discrimination drills.
    /// </summary>
    Task RecordConfusionAsync(string targetId, string pickedId);

    /// <summary>
    /// Returns the words most often picked when <paramref name="targetId"/> was the correct
    /// answer, ordered by descending count. Synchronous so it's cheap to call from the
    /// per-question distractor scorer.
    /// </summary>
    IReadOnlyList<(string PickedId, int Count)> GetTopConfusersFor(string targetId, int limit);

    /// <summary>
    /// Set of word ids that have at least one recorded confusion against them. Used by the
    /// validation pass to enumerate eligible re-test candidates without scanning every pair.
    /// </summary>
    IReadOnlyCollection<string> GetConfusedTargetIds();

    /// <summary>
    /// Nudges every criterion score for <paramref name="wordId"/> by <paramref name="delta"/>
    /// percentage points (clamped to 0–100). Creates the proficiency record if it doesn't
    /// exist yet. Used by the manual +/- controls on the Progress page.
    /// </summary>
    Task AdjustScoresAsync(string wordId, double delta);
}
