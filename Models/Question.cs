namespace LearnJP.Models;

public enum QuestionDirection { JapaneseToEnglish, EnglishToJapanese }

public enum JapaneseDisplayMode
{
    KanjiWithFurigana,
    HiraganaOnly,
    KanjiOnly,
    RomajiOnly
}

public sealed class QuestionOption
{
    public required Word Word { get; init; }
    public required string DisplayText { get; init; }
    public bool IsCorrect { get; init; }
}

public sealed class Question
{
    public required Word Target { get; init; }
    public required QuestionDirection Direction { get; init; }
    public required ProficiencyCriterion Criterion { get; init; }
    public required JapaneseDisplayMode DisplayMode { get; init; }
    public required string Prompt { get; init; }
    public string? PromptFurigana { get; init; }
    public required IReadOnlyList<QuestionOption> Options { get; init; }
    public required string TtsText { get; init; }
    public required string TtsLocaleTag { get; init; }
    public required double TargetProficiencyAtAsk { get; init; }
    /// <summary>True when the picker drew this word from the strategy's active focus set.</summary>
    public bool IsInReinforcementSet { get; init; }

    /// <summary>
    /// True when this pick was forced by the proficiency-validation pass — i.e. a word with
    /// high proficiency that the user has historically confused, surfaced again to confirm the
    /// score is real. The distractor bias guarantees the top confuser appears in the options.
    /// </summary>
    public bool IsValidation { get; init; }
}
