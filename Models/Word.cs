using System.Text.Json.Serialization;

namespace LearnJP.Models;

/// <summary>
/// A single vocabulary entry. The user's writing-system data lives in <see cref="Forms"/> —
/// a positional array indexed by whatever role names the active <see cref="LanguagePack"/>
/// declared (e.g. JP packs declare three slots <c>romaji / kana / kanji</c>; most other
/// languages declare a single slot). Call sites should never index <see cref="Forms"/>
/// directly — they ask the active <see cref="LanguageBehavior"/>, which knows the role
/// names and decides which form to expose for a given purpose (display, reading, TTS, etc.).
/// </summary>
public sealed class Word
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Per-pack form variants. The pack's <see cref="LanguagePack.Forms"/> declaration is
    /// the shared role→index lookup; each word just carries the values. Loaded by the vocab
    /// loader from the JSON keys the pack lists. Defaults to an empty array — readers must
    /// tolerate missing slots and treat them as empty strings.
    /// </summary>
    [JsonIgnore]
    public string[] Forms { get; set; } = Array.Empty<string>();

    [JsonPropertyName("meanings")]
    public List<string> Meanings { get; set; } = new();

    [JsonPropertyName("pos")]
    public string PartOfSpeech { get; set; } = string.Empty;

    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = new();

    [JsonPropertyName("frequencyRank")]
    public int FrequencyRank { get; set; } = int.MaxValue;

    public string PrimaryMeaning => Meanings.Count > 0 ? Meanings[0] : string.Empty;
    public string MeaningsJoined => string.Join(", ", Meanings);

    /// <summary>Returns the form at <paramref name="index"/> or empty if out of range.
    /// Out-of-range is intentionally non-throwing — a pack may declare three slots while a
    /// particular word has only filled the first one.</summary>
    public string FormAt(int index) =>
        index >= 0 && index < Forms.Length ? (Forms[index] ?? string.Empty) : string.Empty;
}
