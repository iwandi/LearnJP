# Learning-design notes

Reference material for the next iteration of the proficiency / scheduling model.
Captured while reviewing the strategy simulator — current approach (proficiency-weighted
picker + simple spaced interval) is reasonable but doesn't model the right targets.

## Goals (in order)

1. Keep the **whole pool** at a uniform retention level, rather than chasing 100% mastery
   on a few words.
2. **Validate** proficiency — when a word looks "known", actively challenge it against its
   hardest confusable so a high score reflects real recall, not flattery from easy distractors.
3. Detect when the user starts confusing similar terms as their pool grows, and intervene
   with targeted discrimination drills.

## Relevant prior art

### FSRS (Free Spaced Repetition Scheduler)
- Used by Anki since 2023; refines SuperMemo SM-17.
- Models each card with **stability** S and **retrievability** R(t) = exp(−t/S).
- Picks the card whose R has dropped closest to a target (e.g. 0.90).
- Interval formula: `t_next = S · ln(target_R) / ln(R_now)`.
- Naturally elevates the whole deck to the chosen retention rate. Replaces our current
  ad-hoc `ComputeInterval` (`2 · 1.6^(overall/12)`), which is monotonic in proficiency
  but doesn't tie to any retention target.
- Reference implementation: github.com/open-spaced-repetition/fsrs4anki (MIT). Math is
  ports of a few hundred lines.

### Discrimination training (interleaving)
- Carvalho & Goldstone, *Memory & Cognition*, 2015 — "When does interleaving practice
  improve learning?" Confusable items consolidate better when interleaved than when
  practised in blocks, because each item becomes a discriminator for the other.
- Direct implication: when a pair (A, B) is confused, schedule them in close succession
  AND make B the distractor when A is the target (and vice versa).

### Desirable difficulty
- Bjork & Bjork, 1992 — practising near the recall boundary consolidates more than easy
  reviews. Already partially exploited by our distractor scaling, but we never *retest*
  a mastered word against its hardest historical confusable.

### Item Response Theory (background)
- Models per-item difficulty as a learnable parameter; pick items at the user's ability
  boundary. We approximate this; FSRS effectively absorbs it via stability fitting.

## Concrete proposals

### A. Confusion matrix (high leverage, low effort — start here)
- New per-pair counter `confusions[correctId][pickedId]` updated on every wrong answer.
- Stored on `IProficiencyStore` (or a sibling store); JSON-persisted alongside proficiency.
- Consumed by:
  - `ScoreDistractor` — bias toward known confusable IDs for the current target.
  - A new "discrimination drill" pick mode that surfaces (A, B) in adjacent turns when a
    pair crosses a threshold (e.g. ≥3 confusions or >25% of A's wrong answers).
- Cost: ~1 day. Ships independently of any scheduling rewrite.

### B. Proficiency validation pass
- When a word's score is high, periodically re-ask it with its top-confusion distractor
  (from the matrix). On fail, drop the score sharply (e.g. multiply by 0.55, same as the
  current wrong-answer penalty in `WordProficiency.RecordResult`).
- Cost: small; depends on (A).

### C. FSRS-lite scheduler
- Replace the four `ProficiencyCriterion` scores with a single recall log per word:
  list of `(timestamp, correct)` events.
- Fit per-word stability S; pick the word with R closest to the target.
- Question direction (JP→EN / EN→JP) becomes a *variant* within the same item, not an
  independent score axis — current split dilutes data.
- Cost: real refactor; only worth it once we have weeks of real-user review data to
  validate the fit.

## Things to skip

- **Graded recall** (easy/hard/wrong buttons). Improves accuracy but adds a per-turn UI
  decision and a button the user has said they don't want.
- **Per-criterion separate stabilities**. Four scores per word costs more storage and
  data dilution than the discrimination signal it provides; the confusion matrix gives
  a cleaner version of the same thing.

## Order of operations

1. Confusion matrix + distractor bias + "swoop in" pair drills. *(A)*
2. Validation pass against top confusion distractor. *(B)*
3. Re-evaluate whether C is still worth it once A+B are in production with real data.
