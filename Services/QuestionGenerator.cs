using LearnJP.Models;

namespace LearnJP.Services;

public sealed class QuestionGenerator : IQuestionGenerator
{
    private readonly IVocabularyService _vocab;
    private readonly IProficiencyStore _store;
    private readonly ISettingsService _settings;
    private readonly ILanguagePackService? _packs;
    private readonly Random _rng = new();
    private string? _lastWordId;

    // Persistent focus sets per strategy. Sized to 18 so each word averages ≈18 picks
    // between repeats — enough variety that "I just saw this 3 questions ago" can't telegraph
    // the answer, but small enough to stay coherent within a session.
    private const int FocusSetTargetSize = 18;
    private readonly HashSet<string> _quickReviewSet = new(StringComparer.Ordinal);
    private readonly HashSet<string> _weakFocusSet = new(StringComparer.Ordinal);

    // Active intake frontier for never-seen vocabulary. With ~1000 terms, letting all unseen
    // words compete at full weight drowns out the words the user is actively learning, so the
    // rotation stalls on a wide-but-shallow sweep. Instead, only the K most-common (lowest
    // FrequencyRank) unseen words are "in scope" at any moment — once one is learned past the
    // mastery floor, the next-most-common unseen word slides in.
    // Instance-scoped so the strategy simulator can sweep it without trampling parallel runs.
    public int NewTermFrontierSize { get; set; } = 12;

    public IReadOnlyList<Word> CurrentNewTermFrontier { get; private set; } = Array.Empty<Word>();

    // Probability that any given pick is hijacked by the validation pass: a high-proficiency
    // word with logged confusions surfaced to confirm the score is real. The distractor bias
    // already pulls the top confuser into the option set, so the user gets a real test.
    public double ValidationPickProbability { get; set; } = 0.08;
    private const double ValidationProficiencyFloor = 75.0;

    /// <summary>
    /// Sort key for FrequencyRank: real ranks are positive (smaller = more common). Both 0 and
    /// int.MaxValue are sentinel "no data" markers from the source vocabulary / cleaner, so they
    /// must sort *last* — otherwise unranked words masquerade as the most common.
    /// </summary>
    private static int FrequencyOrderKey(int rank) => rank <= 0 ? int.MaxValue : rank;

    public QuestionGenerator(IVocabularyService vocab, IProficiencyStore store, ISettingsService settings, ILanguagePackService? packs = null)
    {
        _vocab = vocab;
        _store = store;
        _settings = settings;
        _packs = packs;
    }

    public async Task<Question?> NextAsync(LearningStrategy strategy = LearningStrategy.Fsrs)
    {
        await _vocab.EnsureLoadedAsync();
        await _store.LoadAsync();

        IReadOnlyList<Word> pool = _vocab.All;
        var include = _settings.ActiveIncludeTags ?? Array.Empty<string>();
        var exclude = _settings.ActiveExcludeTags ?? Array.Empty<string>();
        var includeSet = new HashSet<string>(include, StringComparer.OrdinalIgnoreCase);
        var excludeSet = new HashSet<string>(exclude, StringComparer.OrdinalIgnoreCase);

        // Kana stays excluded from general practice unless the user explicitly opts in via
        // the include list — matches the prior "no filter excludes kana" behaviour.
        bool glyphAllowed = _packs?.Active?.GlyphTags.Any() ?? false;

        IEnumerable<Word> filteredEnum = pool;
        if (includeSet.Count > 0)
            filteredEnum = filteredEnum.Where(w => w.Tags.Any(t => includeSet.Contains(t)));
        if (excludeSet.Count > 0)
            filteredEnum = filteredEnum.Where(w => !w.Tags.Any(t => excludeSet.Contains(t)));
        if (!glyphAllowed)
            filteredEnum = filteredEnum.Where(w => CategoryOf(w) != WordCategory.Glyph);

        var filtered = filteredEnum.ToList();
        // Need at least one target plus one distractor — fall back to the default
        // (kana-excluded) pool if the user's filter yields too few hits.
        pool = filtered.Count >= 2
            ? filtered
            : _vocab.All.Where(w => CategoryOf(w) != WordCategory.Glyph).ToList();

        if (pool.Count < 2) return null;

        // Compute the active new-term frontier once per call so consumers (e.g. TTS prefetch)
        // can warm caches for the words about to surface.
        // Lower FrequencyRank = more common; rank 0 / MaxValue means "no data" and is sorted last.
        CurrentNewTermFrontier = pool
            .Where(w => _store.Get(w.Id).TotalSeen == 0)
            .OrderBy(w => FrequencyOrderKey(w.FrequencyRank))
            .Take(NewTermFrontierSize)
            .ToList();

        // Validation pass: with a small probability, override the strategy and surface a
        // high-proficiency word the user has historically confused. Catches over-rated words
        // before they slip out of rotation entirely.
        Word? target = null;
        bool isValidation = false;
        if (_rng.NextDouble() < ValidationPickProbability)
        {
            target = PickValidationTarget(pool);
            isValidation = target is not null;
        }

        target ??= strategy switch
        {
            LearningStrategy.Spaced      => PickTargetSpaced(pool),
            LearningStrategy.QuickReview => PickTargetQuickReview(pool),
            LearningStrategy.WeakFocus   => PickTargetWeakFocus(pool),
            LearningStrategy.Fsrs        => PickTargetFsrs(pool),
            _                            => PickTarget(pool)
        };
        if (target is null) return null;

        var prof = _store.Get(target.Id);
        var criterion = PickCriterion(prof);
        var direction = DirectionFor(criterion);
        var optionCount = OptionCountFor(prof.Overall);

        // All language-specific rendering routes through the active pack's behaviour. The
        // generic fallback (used by the StrategySim when no pack is wired) is a behaviour
        // that just reads Forms[0] — produces sensible prompts/options without crashing.
        var pack = _packs?.Active;
        var behavior = pack?.Behavior ?? _genericBehavior;
        var flags = pack is not null ? _settings.DisplayFlagsFor(pack.Id) : _emptyFlags;

        var distractors = PickDistractors(target, criterion, prof.Overall, optionCount - 1, pool, behavior);
        var options = BuildOptions(target, distractors, direction, behavior, flags);
        var rendered = behavior.RenderPrompt(target, direction, prof.Overall, flags);
        var ttsText = behavior.TtsText(target);

        _lastWordId = target.Id;

        return new Question
        {
            Target = target,
            Direction = direction,
            Criterion = criterion,
            Prompt = rendered.Prompt,
            PromptFurigana = rendered.Furigana,
            Options = options,
            TtsText = ttsText,
            TtsWordId = target.Id,
            TargetProficiencyAtAsk = prof.Overall,
            IsInReinforcementSet = IsInActiveFocusSet(strategy, target.Id),
            IsValidation = isValidation
        };
    }

    // Used when the generator runs without a wired language pack (e.g. the strategy sim).
    // Provides language-neutral defaults — Forms[0] for everything, no display flags.
    private static readonly LanguageBehavior _genericBehavior = new GenericBehavior(null);
    private static readonly IDisplayFlags _emptyFlags = new EmptyFlags();
    private sealed class EmptyFlags : IDisplayFlags { public bool Get(string key, bool def = false) => def; }

    /// <summary>
    /// FSRS-style scheduler: pick the word whose retrievability has dropped closest below
    /// the target (default 0.90). Falls back to the new-term frontier when nothing is due,
    /// so the deck keeps growing instead of stalling once everything is at target.
    /// </summary>
    private Word? PickTargetFsrs(IReadOnlyList<Word> pool)
    {
        var now = DateTime.UtcNow;
        // bestDue   — word that has already crossed below the retrievability target (gap > 0).
        // nextDue   — closest-to-threshold word when nothing is overdue yet; used as a
        //             tiebreak fallback so we don't pick completely at random in a mature deck.
        Word? bestDue = null;
        double bestGap = double.NegativeInfinity;
        Word? nextDue = null;
        double nextDueGap = double.NegativeInfinity;
        var unseen = new List<Word>();

        foreach (var w in pool)
        {
            if (w.Id == _lastWordId) continue;
            var s = _store.GetFsrsState(w.Id);
            if (!s.HasState) { unseen.Add(w); continue; }
            var r = Fsrs.Retrievability(s, now);
            var gap = Fsrs.TargetRetrievability - r;
            if (gap > 0)
            {
                // Word is overdue: prefer the one furthest below threshold (largest positive gap).
                if (gap > bestGap) { bestGap = gap; bestDue = w; }
            }
            else
            {
                // Word is not yet due: track the one closest to threshold (largest negative gap)
                // so it surfaces next once the deck has no overdue items.
                if (gap > nextDueGap) { nextDueGap = gap; nextDue = w; }
            }
        }

        // Pull a frontier-new word about a quarter of the time so vocabulary keeps unlocking.
        // When nothing is overdue (bestDue is null), always pick a new word from the frontier.
        if (unseen.Count > 0 && (bestDue is null || _rng.NextDouble() < 0.25))
        {
            var frontier = unseen.OrderBy(w => FrequencyOrderKey(w.FrequencyRank)).Take(NewTermFrontierSize).ToList();
            return frontier[_rng.Next(frontier.Count)];
        }
        return bestDue ?? nextDue ?? (pool.Count > 0 ? pool[_rng.Next(pool.Count)] : null);
    }

    /// <summary>
    /// Picks a high-proficiency word with logged confusions so the validation pass can verify
    /// the score is real. Returns null if no pool word qualifies.
    /// </summary>
    private Word? PickValidationTarget(IReadOnlyList<Word> pool)
    {
        var confused = _store.GetConfusedTargetIds();
        if (confused.Count == 0) return null;

        var candidates = new List<Word>();
        foreach (var w in pool)
        {
            if (w.Id == _lastWordId) continue;
            if (!confused.Contains(w.Id)) continue;
            if (_store.Get(w.Id).Overall < ValidationProficiencyFloor) continue;
            candidates.Add(w);
        }
        return candidates.Count == 0 ? null : candidates[_rng.Next(candidates.Count)];
    }

    /// <summary>
    /// Turn-based spaced repetition. A word becomes a candidate when the global turn counter
    /// reaches its <see cref="WordProficiency.NextDueAtTurn"/>. Among candidates, more-overdue
    /// words win; never-seen words come in if no due words remain. Distractor selection is unchanged
    /// (still drawn from the full vocabulary), so the option list does not telegraph the focus set.
    /// </summary>
    private Word? PickTargetSpaced(IReadOnlyList<Word> pool)
    {
        var turn = _store.TurnsAsked;

        // Bucket 1: due words, weighted by overdue-ness.
        var due = new List<(Word w, double weight)>();
        // Bucket 2: never-seen words (no schedule yet); used when nothing is due.
        var unseen = new List<Word>();
        // Bucket 3: a tiny background mix of upcoming words so the schedule doesn't ossify.
        var background = new List<(Word w, double weight)>();

        foreach (var w in pool)
        {
            if (w.Id == _lastWordId) continue;
            var p = _store.Get(w.Id);
            if (p.IsReinforced)
            {
                // Pinned words always go in the due bucket with maximum weight.
                due.Add((w, 60.0));
                continue;
            }
            if (p.TotalSeen == 0) { unseen.Add(w); continue; }
            var overdue = turn - p.NextDueAtTurn;
            if (overdue >= 0)
                due.Add((w, 1.0 + Math.Min(overdue, 50))); // cap so a long pause doesn't dominate
            else if (-overdue <= 5)
                background.Add((w, 0.15));                 // about-to-be-due, low weight
        }

        // Restrict the unseen bucket to the active intake frontier (most-common words first),
        // so a long tail of 900+ unseen entries can't drown out the words being learned.
        // FrequencyOrderKey treats 0 / MaxValue as "no data" so they sort last, not first.
        if (unseen.Count > NewTermFrontierSize)
        {
            unseen = unseen.OrderBy(w => FrequencyOrderKey(w.FrequencyRank)).Take(NewTermFrontierSize).ToList();
        }

        // Prefer due words.
        if (due.Count > 0)
        {
            // Occasionally still mix in a frontier-new word so vocabulary keeps unlocking
            // even when the schedule is full of overdue work; kept low so reinforcement wins.
            if (unseen.Count > 0 && _rng.NextDouble() < 0.15)
                return unseen[_rng.Next(unseen.Count)];
            return WeightedPick(due);
        }

        // Nothing due — pull from the frontier most of the time, otherwise sample a
        // background "about-to-be-due" word for variety.
        if (unseen.Count > 0 && (background.Count == 0 || _rng.NextDouble() < 0.5))
            return unseen[_rng.Next(unseen.Count)];

        if (background.Count > 0) return WeightedPick(background);

        // Fallback: just pick the lowest-proficiency word we know.
        return pool.OrderBy(w => _store.Get(w.Id).Overall).ThenBy(_ => _rng.Next()).FirstOrDefault();
    }

    /// <summary>
    /// Quickly cycles through words the user already knows. Maintains a persistent focus set of
    /// ≈18 high-proficiency words and rotates through them by least-recently-seen, so the user
    /// gets a meaningful sweep instead of the same 5 words on repeat.
    /// </summary>
    private Word? PickTargetQuickReview(IReadOnlyList<Word> pool)
    {
        EnsureQuickReviewSet(pool);

        var candidates = pool
            .Where(w => w.Id != _lastWordId && _quickReviewSet.Contains(w.Id))
            .Select(w => (w, p: _store.Get(w.Id)))
            .OrderBy(t => t.p.LastSeenUtc ?? DateTime.MinValue)
            .ToList();

        // Bias to the oldest 8 in the set so we sweep the whole set across ~18 picks
        // without forming an obvious sequential pattern.
        if (candidates.Count == 0) return PickTarget(pool);
        var top = candidates.Take(8).ToList();
        var items = new List<(Word w, double weight)>(top.Count);
        for (int i = 0; i < top.Count; i++)
            items.Add((top[i].w, top.Count - i));
        return WeightedPick(items);
    }

    /// <summary>
    /// Drills the weakest words to lift their proficiency. Maintains a persistent focus set of
    /// ≈18 weak-or-new words; words "graduate" out as their overall climbs past 75, and fresh
    /// weaknesses (or never-seen words) take their place automatically.
    /// </summary>
    private Word? PickTargetWeakFocus(IReadOnlyList<Word> pool)
    {
        EnsureWeakFocusSet(pool);

        var candidates = pool
            .Where(w => w.Id != _lastWordId && _weakFocusSet.Contains(w.Id))
            .Select(w => (w, p: _store.Get(w.Id)))
            .OrderBy(t => t.p.Overall)
            .ThenBy(t => t.p.LastSeenUtc ?? DateTime.MinValue)
            .ToList();

        if (candidates.Count == 0) return PickTarget(pool);
        var top = candidates.Take(10).ToList();
        var items = new List<(Word w, double weight)>(top.Count);
        for (int i = 0; i < top.Count; i++)
            items.Add((top[i].w, top.Count - i));
        return WeightedPick(items);
    }

    /// <summary>Refills the QuickReview focus set so it always holds ≈18 known words.</summary>
    private void EnsureQuickReviewSet(IReadOnlyList<Word> pool)
    {
        var poolIds = new HashSet<string>(pool.Select(w => w.Id), StringComparer.Ordinal);
        // Drop members that fall below the floor or leave the pool entirely.
        _quickReviewSet.RemoveWhere(id =>
            !poolIds.Contains(id) || _store.Get(id).Overall < 30.0);

        if (_quickReviewSet.Count >= FocusSetTargetSize) return;

        // Tiered threshold so the set fills even when the user hasn't built up many strong words.
        foreach (var threshold in new[] { 70.0, 50.0, 30.0 })
        {
            var candidates = pool
                .Where(w => !_quickReviewSet.Contains(w.Id))
                .Select(w => (w, p: _store.Get(w.Id)))
                .Where(t => t.p.TotalSeen > 0 && t.p.Overall >= threshold)
                .OrderByDescending(t => t.p.Overall)
                .ThenBy(_ => _rng.Next())
                .ToList();
            foreach (var (w, _) in candidates)
            {
                if (_quickReviewSet.Count >= FocusSetTargetSize) return;
                _quickReviewSet.Add(w.Id);
            }
        }
    }

    /// <summary>Refills the WeakFocus set so it always holds ≈18 below-mastery (or new) words.</summary>
    private void EnsureWeakFocusSet(IReadOnlyList<Word> pool)
    {
        var poolIds = new HashSet<string>(pool.Select(w => w.Id), StringComparer.Ordinal);
        // Graduate words that have climbed past 75 — they're no longer "weak".
        _weakFocusSet.RemoveWhere(id =>
            !poolIds.Contains(id) || _store.Get(id).Overall >= 75.0);

        if (_weakFocusSet.Count >= FocusSetTargetSize) return;

        // First pass: weakest seen words.
        var seenWeak = pool
            .Where(w => !_weakFocusSet.Contains(w.Id))
            .Select(w => (w, p: _store.Get(w.Id)))
            .Where(t => t.p.TotalSeen > 0 && t.p.Overall < 75.0)
            .OrderBy(t => t.p.Overall)
            .ToList();
        foreach (var (w, _) in seenWeak)
        {
            if (_weakFocusSet.Count >= FocusSetTargetSize) return;
            _weakFocusSet.Add(w.Id);
        }

        // Pad with never-seen words so the drill keeps unlocking new vocabulary.
        var unseen = pool
            .Where(w => !_weakFocusSet.Contains(w.Id))
            .Where(w => _store.Get(w.Id).TotalSeen == 0)
            .OrderBy(_ => _rng.Next())
            .ToList();
        foreach (var w in unseen)
        {
            if (_weakFocusSet.Count >= FocusSetTargetSize) return;
            _weakFocusSet.Add(w.Id);
        }
    }

    private bool IsInActiveFocusSet(LearningStrategy strategy, string wordId) => strategy switch
    {
        LearningStrategy.QuickReview => _quickReviewSet.Contains(wordId),
        LearningStrategy.WeakFocus   => _weakFocusSet.Contains(wordId),
        _                            => false
    };

    private Word WeightedPick(List<(Word w, double weight)> items)
    {
        double total = items.Sum(t => t.weight);
        if (total <= 0) return items[_rng.Next(items.Count)].w;
        var r = _rng.NextDouble() * total;
        double acc = 0;
        foreach (var (w, weight) in items)
        {
            acc += weight;
            if (r <= acc) return w;
        }
        return items[^1].w;
    }

    private Word? PickTarget(IReadOnlyList<Word> pool)
    {
        // Use the precomputed intake frontier: the K most-common unseen words. Only these
        // compete for "new word" picks; other unseen words sit dormant at a tiny floor weight
        // so they're occasionally surfaced but don't crowd out in-progress learning.
        var frontier = new HashSet<string>(CurrentNewTermFrontier.Select(w => w.Id), StringComparer.Ordinal);

        var weights = new double[pool.Count];
        double total = 0;
        for (int i = 0; i < pool.Count; i++)
        {
            var w = pool[i];
            if (w.Id == _lastWordId) { weights[i] = 0.05; total += 0.05; continue; }
            var prof = _store.Get(w.Id);
            var p = prof.Overall;
            double weight;
            if (prof.TotalSeen == 0)
            {
                // Unseen: full weight only inside the frontier; otherwise dormant.
                if (frontier.Contains(w.Id))
                {
                    var key = FrequencyOrderKey(w.FrequencyRank);
                    var freqBoost = key == int.MaxValue ? 1.0 : 1.0 + (1.0 / Math.Sqrt(key + 1));
                    weight = 0.8 * freqBoost; // ~1.0–1.6
                }
                else
                {
                    weight = 0.02;
                }
            }
            else
            {
                // Seen: dominate while learning, taper as proficiency climbs, never zero.
                // 5.0 at Overall=0 (drilling a word that keeps slipping), 1.0 at Overall=100.
                weight = 1.0 + 4.0 * Math.Max(0.0, 1.0 - (p / 100.0));
            }
            // Pinned reinforcement override: dominate the weight so it surfaces frequently.
            if (prof.IsReinforced) weight = Math.Max(weight, 8.0);
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
        ProficiencyCriterion.TargetToBase => QuestionDirection.TargetToBase,
        ProficiencyCriterion.SimilarSoundDifferentiation => QuestionDirection.TargetToBase,
        ProficiencyCriterion.BaseToTarget => QuestionDirection.BaseToTarget,
        ProficiencyCriterion.SimilarMeaningDifferentiation => QuestionDirection.BaseToTarget,
        _ => QuestionDirection.TargetToBase
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

    private List<Word> PickDistractors(Word target, ProficiencyCriterion criterion, double overall, int count, IReadOnlyList<Word> pool, LanguageBehavior behavior)
    {
        if (count <= 0) return new();

        var targetCategory = CategoryOf(target);
        var candidates = pool
            .Where(w => w.Id != target.Id && CategoryOf(w) == targetCategory)
            .ToList();

        // Pull the user's known confusions for this target so we can bias toward them —
        // lifts whichever distractor the user has historically mistaken for this word.
        var confusionByWordId = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (id, n) in _store.GetTopConfusersFor(target.Id, 8))
            confusionByWordId[id] = n;

        // Score every candidate by suitability for this criterion + proficiency level.
        var scored = candidates
            .Select(w => (Word: w, Score: ScoreDistractor(w, target, criterion, overall, behavior)
                                          + (confusionByWordId.TryGetValue(w.Id, out var n) ? 40.0 + n * 5.0 : 0.0)))
            .OrderByDescending(t => t.Score)
            .ThenBy(_ => _rng.Next())
            .ToList();

        // Take top portion and randomize among them so the same distractors don't repeat.
        var topPortion = scored.Take(Math.Max(count * 4, 12)).ToList();

        var picked = new List<Word>();
        var usedMeanings = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { target.PrimaryMeaning };
        // De-dupe options that share the same phonetic form (e.g. JP homophones), since the
        // listener can't tell them apart. The phonetic form is whatever the language's
        // behaviour considers canonical for sound comparison (kana for JP, primary form
        // for romance languages).
        var usedPhonetic = new HashSet<string>(StringComparer.Ordinal) { behavior.PhoneticForm(target) };

        foreach (var (w, _) in topPortion.OrderBy(_ => _rng.Next()))
        {
            if (picked.Count >= count) break;
            if (usedMeanings.Contains(w.PrimaryMeaning)) continue;
            var phon = behavior.PhoneticForm(w);
            if (!string.IsNullOrEmpty(phon) && usedPhonetic.Contains(phon)) continue;
            // Avoid synonyms of target.
            if (target.Meanings.Any(m => w.Meanings.Contains(m, StringComparer.OrdinalIgnoreCase))) continue;
            picked.Add(w);
            usedMeanings.Add(w.PrimaryMeaning);
            if (!string.IsNullOrEmpty(phon)) usedPhonetic.Add(phon);
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

    private double ScoreDistractor(Word w, Word target, ProficiencyCriterion criterion, double overall, LanguageBehavior behavior)
    {
        var prof = _store.Get(w.Id).Overall;
        double score = 0;

        switch (criterion)
        {
            case ProficiencyCriterion.SimilarSoundDifferentiation:
                score += SoundSimilarity(behavior.PhoneticForm(w), behavior.PhoneticForm(target)) * 100;
                // Prefer somewhat-known words so the contrast is meaningful.
                score += Math.Min(prof, 50) * 0.3;
                break;

            case ProficiencyCriterion.SimilarMeaningDifferentiation:
                score += MeaningSimilarity(w, target) * 100;
                score += SamePosBonus(w, target);
                score += Math.Min(prof, 50) * 0.3;
                break;

            case ProficiencyCriterion.TargetToBase:
            case ProficiencyCriterion.BaseToTarget:
            default:
                // Low proficiency → prefer well-known, distinct words. High → prefer same-POS, less-known, similar.
                if (overall < 35)
                {
                    score += prof; // higher proficiency = more distinct/familiar
                    score -= MeaningSimilarity(w, target) * 60;
                    score -= SoundSimilarity(behavior.PhoneticForm(w), behavior.PhoneticForm(target)) * 40;
                }
                else
                {
                    score += SamePosBonus(w, target);
                    score += SoundSimilarity(behavior.PhoneticForm(w), behavior.PhoneticForm(target)) * 40;
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

    /// <summary>
    /// Glyph entries form their own pool: kana / jamo / character drills only contrast with
    /// other glyphs, and general vocabulary never gets one of them as a distractor. Detection
    /// is delegated to the active language's behaviour module; legacy hiragana/katakana
    /// id-prefix detection is applied as a fallback so out-of-band callers (e.g. the sim
    /// that runs without a language pack) still behave correctly.
    /// </summary>
    private enum WordCategory { Glyph, General }

    private WordCategory CategoryOf(Word w)
    {
        var pack = _packs?.Active;
        if (pack is not null && pack.Behavior.IsGlyphEntry(w, pack.GlyphTags))
            return WordCategory.Glyph;
        return WordCategory.General;
    }

    private List<QuestionOption> BuildOptions(Word target, List<Word> distractors, QuestionDirection dir, LanguageBehavior behavior, IDisplayFlags flags)
    {
        var options = new List<QuestionOption>(distractors.Count + 1);

        // Idle state: meaning side shows the meaning; target side shows whatever the active
        // language considers a presentable option label (e.g. JP "kanji (kana)" vs IT "ciao").
        // Revealed state always combines the target form with the meaning.
        string Idle(Word w) => dir == QuestionDirection.TargetToBase
            ? w.PrimaryMeaning
            : behavior.OptionDisplay(w, flags);
        string Revealed(Word w) => behavior.RevealedDisplay(w, flags);

        options.Add(new QuestionOption
        {
            Word = target,
            DisplayText = Idle(target),
            RevealedText = Revealed(target),
            IsCorrect = true
        });
        foreach (var d in distractors)
            options.Add(new QuestionOption
            {
                Word = d,
                DisplayText = Idle(d),
                RevealedText = Revealed(d),
                IsCorrect = false
            });

        // Shuffle.
        for (int i = options.Count - 1; i > 0; i--)
        {
            var j = _rng.Next(i + 1);
            (options[i], options[j]) = (options[j], options[i]);
        }
        return options;
    }
}
