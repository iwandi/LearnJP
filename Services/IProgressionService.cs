namespace LearnJP.Services;

/// <summary>
/// Evaluates which stages of the active language pack's progression ladder have been
/// unlocked based on the user's current word proficiency.
/// </summary>
public interface IProgressionService
{
    /// <summary>
    /// Returns the tags of all unlocked progression stages for the active language pack,
    /// in order. Stage 0 is always included. Each later stage is included only when the
    /// fraction of <see cref="LearnJP.Models.WordProficiency.IsKnown"/> words in the
    /// previous stage meets that stage's unlock threshold.
    /// Returns an empty list when the active pack has no progression defined.
    /// </summary>
    IReadOnlyList<string> GetUnlockedTags();
}
