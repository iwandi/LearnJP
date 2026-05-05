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
    private readonly ISettingsService _settings;

    public ProgressionService(ILanguagePackService packs, IVocabularyService vocab, IProficiencyStore store, ISettingsService settings)
    {
        _packs = packs;
        _vocab = vocab;
        _store = store;
        _settings = settings;
    }

    /// <inheritdoc />
    public IReadOnlyList<string> GetUnlockedTags()
    {
        var pack = _packs.Active;
        if (pack is null || pack.Progression.Count == 0)
            return Array.Empty<string>();

        bool glyphsEnabled = _settings.GetDisplayFlag(pack.Id, LanguageBehavior.FlagIncludeGlyphs, false);
        var glyphTags = pack.GlyphTags;

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

            // When Learn Kana is disabled, glyph stages are treated as auto-passed so they
            // don't block the rest of the progression ladder.
            bool isSkippedGlyph = !glyphsEnabled &&
                glyphTags.Contains(stage.Tag, StringComparer.OrdinalIgnoreCase);

            if (i == 0 || isSkippedGlyph)
            {
                // First stage is always unlocked; skipped glyph stages are auto-passed.
                unlocked.Add(stage.Tag);
                continue;
            }

            // Stage N unlocks when the previous stage's known fraction meets the threshold.
            var prevTag = pack.Progression[i - 1].Tag;
            if (string.IsNullOrEmpty(prevTag)) continue;

            // If the immediately-preceding stage was a skipped glyph stage, treat it as
            // fully mastered so it doesn't block this stage.
            bool prevIsSkippedGlyph = !glyphsEnabled &&
                glyphTags.Contains(prevTag, StringComparer.OrdinalIgnoreCase);
            if (prevIsSkippedGlyph)
            {
                unlocked.Add(stage.Tag);
                continue;
            }

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
