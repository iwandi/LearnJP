namespace LearnJP.Models;

public enum QuestionDirection { TargetToBase, BaseToTarget }

public sealed class QuestionOption
{
    public required Word Word { get; init; }
    /// <summary>Pre-rendered text shown while the option is in its idle (unanswered) state.</summary>
    public required string DisplayText { get; init; }
    /// <summary>Pre-rendered text shown after the user has answered — surfaces both target-language
    /// form and meaning so distractors are verifiable at a glance.</summary>
    public required string RevealedText { get; init; }
    public bool IsCorrect { get; init; }
}

public sealed class Question
{
    public required Word Target { get; init; }
    public required QuestionDirection Direction { get; init; }
    public required ProficiencyCriterion Criterion { get; init; }
    public required string Prompt { get; init; }
    public string? PromptFurigana { get; init; }
    public required IReadOnlyList<QuestionOption> Options { get; init; }
    public required string TtsText { get; init; }
    /// <summary>Vocabulary word ID of the target, used to resolve pre-generated bundled TTS
    /// audio without hashing. Matches the file name under
    /// <c>tts-assets/&lt;provider&gt;/&lt;lang&gt;/&lt;voice&gt;/&lt;wordId&gt;.&lt;ext&gt;</c>.</summary>
    public required string TtsWordId { get; init; }
    public required double TargetProficiencyAtAsk { get; init; }
    /// <summary>True when the picker drew this word from the strategy's active focus set.</summary>
    public bool IsInReinforcementSet { get; init; }

    /// <summary>UTC timestamp captured when the question was generated. Subtracted from the
    /// click time to derive the user's response latency, which feeds the FSRS time→grade map.</summary>
    public DateTime StartedUtc { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// True when this pick was forced by the proficiency-validation pass — i.e. a word with
    /// high proficiency that the user has historically confused, surfaced again to confirm the
    /// score is real. The distractor bias guarantees the top confuser appears in the options.
    /// </summary>
    public bool IsValidation { get; init; }
}
