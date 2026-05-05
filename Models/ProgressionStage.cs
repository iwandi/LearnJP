using System.Text.Json.Serialization;

namespace LearnJP.Models;

/// <summary>
/// One rung in a language pack's ordered progression ladder.
/// Stage 0 is always unlocked. Stage N unlocks when the fraction of <see cref="WordProficiency.IsKnown"/>
/// words in stage N-1 reaches <see cref="UnlockThreshold"/>.
/// </summary>
public sealed class ProgressionStage
{
    /// <summary>The vocabulary tag this stage maps to (e.g. "n5", "hiragana").</summary>
    [JsonPropertyName("tag")]
    public string Tag { get; set; } = string.Empty;

    /// <summary>
    /// Fraction (0–1) of the *previous* stage's words that must be <see cref="WordProficiency.IsKnown"/>
    /// before this stage unlocks. Ignored for stage 0, which is always unlocked.
    /// </summary>
    [JsonPropertyName("unlockThreshold")]
    public double UnlockThreshold { get; set; } = 0.8;
}
