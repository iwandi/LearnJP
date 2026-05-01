using System.Text.Json.Serialization;

namespace LearnJP.Models;

public sealed class Word
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("kanji")]
    public string Kanji { get; set; } = string.Empty;

    [JsonPropertyName("kana")]
    public string Kana { get; set; } = string.Empty;

    [JsonPropertyName("romaji")]
    public string Romaji { get; set; } = string.Empty;

    [JsonPropertyName("meanings")]
    public List<string> Meanings { get; set; } = new();

    [JsonPropertyName("pos")]
    public string PartOfSpeech { get; set; } = string.Empty;

    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = new();

    [JsonPropertyName("frequencyRank")]
    public int FrequencyRank { get; set; } = int.MaxValue;

    public string DisplayKanji => string.IsNullOrWhiteSpace(Kanji) ? Kana : Kanji;
    public string PrimaryMeaning => Meanings.Count > 0 ? Meanings[0] : string.Empty;
    public string MeaningsJoined => string.Join(", ", Meanings);
}
