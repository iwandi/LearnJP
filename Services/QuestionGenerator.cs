using LearnJP.Models;

namespace LearnJP.Services;

public sealed class QuestionGenerator : IQuestionGenerator
{
    private readonly IVocabularyService _vocab;
    private readonly IProficiencyStore _store;
    private readonly ISettingsService _settings;
    private readonly Random _rng = new();
    private string? _lastWordId;

    public QuestionGenerator(IVocabularyService vocab, IProficiencyStore store, ISettingsService settings)
    {
        _vocab = vocab;
        _store = store;
        _settings = settings;
    }

    public async Task<Question?> NextAsync()
    {
        await _vocab.EnsureLoadedAsync();
        await _store.LoadAsync();

        var pool = _vocab.All;
        if (pool.Count < 4) return null;

        var target = PickTarget(pool);
        if (target is null) return null;

        var prof = _store.Get(target.Id);
        var criterion = PickCriterion(prof);
        var direction = DirectionFor(criterion);
        var optionCount = OptionCountFor(prof.Overall);
        var displayMode = DisplayModeFor(prof.Overall, direction);

        var distractors = PickDistractors(target, criterion, prof.Overall, optionCount - 1, pool);
        var options = BuildOptions(target, distractors, direction);
        var (prompt, furigana, ttsText, ttsLocale) = BuildPrompt(target, direction, displayMode);

        _lastWordId = target.Id;

        return new Question
        {
            Target = target,
            Direction = direction,
            Criterion = criterion,
            DisplayMode = displayMode,
            Prompt = prompt,
            PromptFurigana = furigana,
            Options = options,
            TtsText = ttsText,
            TtsLocaleTag = ttsLocale,
            TargetProficiencyAtAsk = prof.Overall
        };
    }

    private Word? PickTarget(IReadOnlyList<Word> pool)
    {
        // Weighted by inverse proficiency, with a small floor so mastered words still rotate in.
        var weights = new double[pool.Count];
        double total = 0;
        for (int i = 0; i < pool.Count; i++)
        {
            var w = pool[i];
            if (w.Id == _lastWordId) { weights[i] = 0.05; total += 0.05; continue; }
            var p = _store.Get(w.Id).Overall;
            // Newer/lower-proficiency words get higher weight; mastered words still rotate.
            var weight = Math.Max(0.05, 1.0 - (p / 100.0));
            // Slightly favor higher-frequency vocabulary when proficiency is similar.
            var freqBoost = w.FrequencyRank == int.MaxValue ? 1.0 : 1.0 + (1.0 / Math.Sqrt(w.FrequencyRank + 1));
            weight *= freqBoost;
            weights[i] = weight;
            total += weight;
        }

        if (total <= 0) return pool[_rng.Next(pool.Count)];
        var r = _rng.NextDouble() * total;
        double acc = 0;
        for (int i = 0; i < pool.Count; i++)
        {
            acc += weights[i];
            if (r <= acc) return pool[i];
        }
        return pool[^1];
    }

    private ProficiencyCriterion PickCriterion(WordProficiency p)
    {
        // Bias toward the weakest criterion, but occasionally sample a stronger one.
        var all = ProficiencyCriterionExtensions.All;
        if (_rng.NextDouble() < 0.2) return all[_rng.Next(all.Count)];

        var weights = new double[all.Count];
        double total = 0;
        for (int i = 0; i < all.Count; i++)
        {
            var weight = Math.Max(0.1, 1.0 - (p.GetScore(all[i]) / 100.0));
            weights[i] = weight;
            total += weight;
        }
        var r = _rng.NextDouble() * total;
        double acc = 0;
        for (int i = 0; i < all.Count; i++)
        {
            acc += weights[i];
            if (r <= acc) return all[i];
        }
        return all[^1];
    }

    private static QuestionDirection DirectionFor(ProficiencyCriterion c) => c switch
    {
        ProficiencyCriterion.JapaneseToEnglish => QuestionDirection.JapaneseToEnglish,
        ProficiencyCriterion.SimilarSoundDifferentiation => QuestionDirection.JapaneseToEnglish,
        ProficiencyCriterion.EnglishToJapanese => QuestionDirection.EnglishToJapanese,
        ProficiencyCriterion.SimilarMeaningDifferentiation => QuestionDirection.EnglishToJapanese,
        _ => QuestionDirection.JapaneseToEnglish
    };

    private static int OptionCountFor(double overall)
    {
        // 2 options at zero proficiency, scaling to 6 at full proficiency.
        if (overall < 15) return 2;
        if (overall < 35) return 3;
        if (overall < 55) return 4;
        if (overall < 75) return 5;
        return 6;
    }

    private JapaneseDisplayMode DisplayModeFor(double overall, QuestionDirection dir)
    {
        if (_settings.RomajiOnly) return JapaneseDisplayMode.RomajiOnly;
        if (dir == QuestionDirection.EnglishToJapanese)
        {
            // Options shown in JP — at low proficiency favor hiragana, otherwise mixed kanji+furigana.
            if (overall < 25) return JapaneseDisplayMode.HiraganaOnly;
            if (overall < 70 || _settings.ForceFurigana) return JapaneseDisplayMode.KanjiWithFurigana;
            return JapaneseDisplayMode.KanjiOnly;
        }
        // JP → EN: Prompt shown in JP.
        if (overall < 25) return JapaneseDisplayMode.HiraganaOnly;
        if (overall < 70 || _settings.ForceFurigana) return JapaneseDisplayMode.KanjiWithFurigana;
        return JapaneseDisplayMode.KanjiOnly;
    }

    private List<Word> PickDistractors(Word target, ProficiencyCriterion criterion, double overall, int count, IReadOnlyList<Word> pool)
    {
        if (count <= 0) return new();

        var candidates = pool.Where(w => w.Id != target.Id).ToList();

        // Score every candidate by suitability for this criterion + proficiency level.
        var scored = candidates
            .Select(w => (Word: w, Score: ScoreDistractor(w, target, criterion, overall)))
            .OrderByDescending(t => t.Score)
            .ThenBy(_ => _rng.Next())
            .ToList();

        // Take top portion and randomize among them so the same distractors don't repeat.
        var topPortion = scored.Take(Math.Max(count * 4, 12)).ToList();

        var picked = new List<Word>();
        var usedMeanings = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { target.PrimaryMeaning };
        var usedKana = new HashSet<string>(StringComparer.Ordinal) { target.Kana };

        foreach (var (w, _) in topPortion.OrderBy(_ => _rng.Next()))
        {
            if (picked.Count >= count) break;
            // Avoid distractors that are functionally indistinguishable (same meaning or same reading).
            if (usedMeanings.Contains(w.PrimaryMeaning)) continue;
            if (usedKana.Contains(w.Kana)) continue;
            // Avoid synonyms of target.
            if (target.Meanings.Any(m => w.Meanings.Contains(m, StringComparer.OrdinalIgnoreCase))) continue;
            picked.Add(w);
            usedMeanings.Add(w.PrimaryMeaning);
            usedKana.Add(w.Kana);
        }

        // Fill remainder with random others if we're short.
        if (picked.Count < count)
        {
            foreach (var w in candidates.OrderBy(_ => _rng.Next()))
            {
                if (picked.Count >= count) break;
                if (picked.Contains(w)) continue;
                if (target.Meanings.Any(m => w.Meanings.Contains(m, StringComparer.OrdinalIgnoreCase))) continue;
                picked.Add(w);
            }
        }
        return picked;
    }

    private double ScoreDistractor(Word w, Word target, ProficiencyCriterion criterion, double overall)
    {
        var prof = _store.Get(w.Id).Overall;
        double score = 0;

        switch (criterion)
        {
            case ProficiencyCriterion.SimilarSoundDifferentiation:
                score += SoundSimilarity(w.Kana, target.Kana) * 100;
                // Prefer somewhat-known words so the contrast is meaningful.
                score += Math.Min(prof, 50) * 0.3;
                break;

            case ProficiencyCriterion.SimilarMeaningDifferentiation:
                score += MeaningSimilarity(w, target) * 100;
                score += SamePosBonus(w, target);
                score += Math.Min(prof, 50) * 0.3;
                break;

            case ProficiencyCriterion.JapaneseToEnglish:
            case ProficiencyCriterion.EnglishToJapanese:
            default:
                // Low proficiency → prefer well-known, distinct words. High → prefer same-POS, less-known, similar.
                if (overall < 35)
                {
                    score += prof; // higher proficiency = more distinct/familiar
                    score -= MeaningSimilarity(w, target) * 60;
                    score -= SoundSimilarity(w.Kana, target.Kana) * 40;
                }
                else
                {
                    score += SamePosBonus(w, target);
                    score += SoundSimilarity(w.Kana, target.Kana) * 40;
                    score += MeaningSimilarity(w, target) * 30;
                    score -= Math.Max(0, prof - 60); // de-prefer fully mastered
                }
                break;
        }

        // Small jitter to keep variety.
        score += _rng.NextDouble() * 2.0;
        return score;
    }

    private static double SoundSimilarity(string a, string b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return 0;
        var maxLen = Math.Max(a.Length, b.Length);
        if (maxLen == 0) return 0;
        var dist = Levenshtein(a, b);
        return 1.0 - ((double)dist / maxLen);
    }

    private static int Levenshtein(string a, string b)
    {
        var dp = new int[a.Length + 1, b.Length + 1];
        for (int i = 0; i <= a.Length; i++) dp[i, 0] = i;
        for (int j = 0; j <= b.Length; j++) dp[0, j] = j;
        for (int i = 1; i <= a.Length; i++)
        for (int j = 1; j <= b.Length; j++)
        {
            var cost = a[i - 1] == b[j - 1] ? 0 : 1;
            dp[i, j] = Math.Min(Math.Min(dp[i - 1, j] + 1, dp[i, j - 1] + 1), dp[i - 1, j - 1] + cost);
        }
        return dp[a.Length, b.Length];
    }

    private static double MeaningSimilarity(Word a, Word b)
    {
        var aWords = TokenizeMeanings(a);
        var bWords = TokenizeMeanings(b);
        if (aWords.Count == 0 || bWords.Count == 0) return 0;
        var inter = aWords.Intersect(bWords).Count();
        var union = aWords.Union(bWords).Count();
        return union == 0 ? 0 : (double)inter / union;
    }

    private static HashSet<string> TokenizeMeanings(Word w)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in w.Meanings)
        {
            foreach (var token in m.Split(new[] { ' ', ',', ';', '/', '(', ')' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var t = token.Trim().ToLowerInvariant();
                if (t.Length <= 2) continue;
                if (t is "to" or "the" or "a" or "an" or "of" or "in") continue;
                set.Add(t);
            }
        }
        return set;
    }

    private static double SamePosBonus(Word a, Word b) =>
        !string.IsNullOrEmpty(a.PartOfSpeech) &&
        string.Equals(a.PartOfSpeech, b.PartOfSpeech, StringComparison.OrdinalIgnoreCase) ? 25.0 : 0.0;

    private List<QuestionOption> BuildOptions(Word target, List<Word> distractors, QuestionDirection dir)
    {
        var options = new List<QuestionOption>(distractors.Count + 1);

        string Display(Word w) => dir == QuestionDirection.JapaneseToEnglish
            ? w.PrimaryMeaning
            : (_settings.RomajiOnly ? w.Romaji : (string.IsNullOrEmpty(w.Kanji) ? w.Kana : $"{w.Kanji}  ({w.Kana})"));

        options.Add(new QuestionOption { Word = target, DisplayText = Display(target), IsCorrect = true });
        foreach (var d in distractors)
            options.Add(new QuestionOption { Word = d, DisplayText = Display(d), IsCorrect = false });

        // Shuffle.
        for (int i = options.Count - 1; i > 0; i--)
        {
            var j = _rng.Next(i + 1);
            (options[i], options[j]) = (options[j], options[i]);
        }
        return options;
    }

    private (string prompt, string? furigana, string ttsText, string ttsLocale) BuildPrompt(
        Word target, QuestionDirection dir, JapaneseDisplayMode mode)
    {
        if (dir == QuestionDirection.EnglishToJapanese)
        {
            return (target.MeaningsJoined, null, target.Kana, "ja");
        }

        // JP → EN, render according to display mode.
        switch (mode)
        {
            case JapaneseDisplayMode.RomajiOnly:
                return (target.Romaji, null, target.Kana, "ja");
            case JapaneseDisplayMode.HiraganaOnly:
                return (target.Kana, null, target.Kana, "ja");
            case JapaneseDisplayMode.KanjiOnly:
                return (string.IsNullOrEmpty(target.Kanji) ? target.Kana : target.Kanji, null, target.Kana, "ja");
            case JapaneseDisplayMode.KanjiWithFurigana:
            default:
                var prompt = string.IsNullOrEmpty(target.Kanji) ? target.Kana : target.Kanji;
                var furi = string.IsNullOrEmpty(target.Kanji) ? null : target.Kana;
                return (prompt, furi, target.Kana, "ja");
        }
    }
}
