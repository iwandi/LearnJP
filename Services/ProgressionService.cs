using LearnJP.Models;

namespace LearnJP.Services;

/// <summary>
/// Evaluates the active language pack's progression ladder against the user's proficiency
/// data and returns the set of currently-unlocked tags.
/// </summary>
public sealed class ProgressionService : IProgressionService
{
    private readonly ILanguagePackService _packs;
    private readonly IVocabularyService _vocab;
    private readonly IProficiencyStore _store;

    public ProgressionService(ILanguagePackService packs, IVocabularyService vocab, IProficiencyStore store)
    {
        _packs = packs;
        _vocab = vocab;
        _store = store;
    }

    /// <inheritdoc />
    public IReadOnlyList<string> GetUnlockedTags()
    {
        var pack = _packs.Active;
        if (pack is null || pack.Progression.Count == 0)
            return Array.Empty<string>();

        // Build a per-tag lookup of known-word counts from the full vocabulary so the check
        // is O(words) once rather than O(stages × words).
        var totalByTag = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var knownByTag = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var word in _vocab.All)
        {
            foreach (var tag in word.Tags)
            {
                if (string.IsNullOrEmpty(tag)) continue;
                totalByTag[tag] = totalByTag.TryGetValue(tag, out var t) ? t + 1 : 1;
                if (_store.Get(word.Id).IsKnown)
                    knownByTag[tag] = knownByTag.TryGetValue(tag, out var k) ? k + 1 : 1;
            }
        }

        var unlocked = new List<string>(pack.Progression.Count);

        for (int i = 0; i < pack.Progression.Count; i++)
        {
            var stage = pack.Progression[i];
            if (string.IsNullOrEmpty(stage.Tag)) continue;

            if (i == 0)
            {
                // First stage is always unlocked.
                unlocked.Add(stage.Tag);
                continue;
            }

            // Stage N unlocks when the previous stage's known fraction meets the threshold.
            var prevTag = pack.Progression[i - 1].Tag;
            if (string.IsNullOrEmpty(prevTag)) continue;

            var total = totalByTag.TryGetValue(prevTag, out var tot) ? tot : 0;
            if (total == 0)
            {
                // No words for the previous tag — treat as fully mastered so the ladder
                // doesn't permanently block users whose pack lacks those words.
                unlocked.Add(stage.Tag);
                continue;
            }

            var known = knownByTag.TryGetValue(prevTag, out var kn) ? kn : 0;
            double fraction = (double)known / total;
            if (fraction >= stage.UnlockThreshold)
                unlocked.Add(stage.Tag);
            else
                break; // Once a stage is locked, all subsequent stages are also locked.
        }

        return unlocked;
    }
}
