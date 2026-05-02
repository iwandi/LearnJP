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
    Task RecordAsync(string wordId, ProficiencyCriterion criterion, bool correct);
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
}
