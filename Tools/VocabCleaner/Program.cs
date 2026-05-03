using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

var path = args.Length > 0
    ? args[0]
    : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "Resources", "Raw", "vocabulary.json"));

if (!File.Exists(path))
{
    Console.Error.WriteLine($"vocabulary.json not found at: {path}");
    return 1;
}

var jsonOpts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
var raw = await File.ReadAllTextAsync(path);
var entries = JsonSerializer.Deserialize<List<JpWord>>(raw, jsonOpts) ?? new();

var stats = new Stats { Input = entries.Count };

// Canonical tag synonyms — collapse before deduping.
var tagAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
{
    ["verbs"]           = "verb",
    ["adjective"]       = "adj",
    ["i-adjective"]     = "i-adj",
    ["na-adjective"]    = "na-adj",
    ["adverbs"]         = "adverb",
    ["colour"]          = "color",
    ["colours"]         = "color",
    ["colors"]          = "color",
    ["jp-n5"]           = "n5",
    ["kana-hiragana"]   = "hiragana",
    ["kana-katakana"]   = "katakana"
};

string Canon(string t) =>
    tagAliases.TryGetValue(t.Trim().ToLowerInvariant(), out var v) ? v : t.Trim().ToLowerInvariant();

List<string> NormaliseTags(IEnumerable<string>? tags, JpWord w)
{
    var set = new SortedSet<string>(StringComparer.Ordinal);
    foreach (var t in tags ?? Enumerable.Empty<string>())
    {
        if (string.IsNullOrWhiteSpace(t)) continue;
        set.Add(Canon(t));
    }
    if (w.Id.StartsWith("n5-", StringComparison.Ordinal)) set.Add("n5");
    if (w.Id.StartsWith("h-",  StringComparison.Ordinal)) set.Add("hiragana");
    if (w.Id.StartsWith("k-",  StringComparison.Ordinal)) set.Add("katakana");
    return set.ToList();
}

string Fingerprint(JpWord w) =>
    $"{(w.Kana ?? "").Trim()}|{(w.Romaji ?? "").Trim().ToLowerInvariant()}|{(w.PrimaryMeaning).Trim().ToLowerInvariant()}";

var seenIds = new HashSet<string>(StringComparer.Ordinal);
var byFingerprint = new Dictionary<string, JpWord>(StringComparer.Ordinal);
var kept = new List<JpWord>(entries.Count);

foreach (var src in entries)
{
    // Trim strings first so id-dedup uses the canonical key
    // (earlier runs accidentally padded ids inside the quotes).
    src.Id     = (src.Id     ?? "").Trim();
    src.Kanji  = (src.Kanji  ?? "").Trim();
    src.Kana   = (src.Kana   ?? "").Trim();
    src.Romaji = (src.Romaji ?? "").Trim();
    src.PartOfSpeech = (src.PartOfSpeech ?? "").Trim();
    src.Meanings = (src.Meanings ?? new()).Select(m => m?.Trim() ?? "").Where(m => m.Length > 0).ToList();

    if (string.IsNullOrWhiteSpace(src.Id) || !seenIds.Add(src.Id))
    {
        stats.DroppedDupId++;
        continue;
    }

    var beforeTags = string.Join('|', src.Tags ?? new());
    src.Tags = NormaliseTags(src.Tags, src);
    if (string.Join('|', src.Tags) != beforeTags) stats.NormalisedTags++;

    if (src.FrequencyRank < 0 || src.FrequencyRank == int.MaxValue)
    {
        src.FrequencyRank = src.Id.StartsWith("n5-") ? 9999 : 0;
        stats.FixedFreq++;
    }

    var fp = Fingerprint(src);
    if (byFingerprint.TryGetValue(fp, out var keeper))
    {
        // Merge tag sets into the existing entry, drop the duplicate.
        var merged = new SortedSet<string>(keeper.Tags, StringComparer.Ordinal);
        foreach (var t in src.Tags) merged.Add(t);
        keeper.Tags = merged.ToList();
        stats.DroppedDupContent++;
        continue;
    }

    byFingerprint[fp] = src;
    kept.Add(src);
}

stats.Output = kept.Count;

// Stable sort: hiragana → katakana → vocabulary, then by id within each group.
int Group(JpWord w) => w.Id switch
{
    var s when s.StartsWith("h-")  => 0,
    var s when s.StartsWith("k-")  => 1,
    _                              => 2
};

kept.Sort((a, b) =>
{
    var g = Group(a) - Group(b); if (g != 0) return g;
    return string.Compare(a.Id, b.Id, StringComparison.Ordinal);
});

// Emit one entry per line with column-aligned padding *outside* the quotes
// (we never alter the actual values).
string J(object? v) => JsonSerializer.Serialize(v);

string IdTok(JpWord w)     => $"\"id\": \"{w.Id}\"";
string KanjiTok(JpWord w)  => $"\"kanji\": \"{w.Kanji}\"";
string KanaTok(JpWord w)   => $"\"kana\": \"{w.Kana}\"";
string RomajiTok(JpWord w) => $"\"romaji\": \"{w.Romaji}\"";
string PosTok(JpWord w)    => $"\"pos\": \"{w.PartOfSpeech}\"";

// Width is in chars (CJK glyphs counted as 1 — the alignment is approximate visually,
// but the file content stays canonical and re-parses identically).
int Wmax(Func<JpWord, string> sel) => kept.Max(w => sel(w).Length);

int idW     = Wmax(IdTok);
int kanjiW  = Wmax(KanjiTok);
int kanaW   = Wmax(KanaTok);
int romajiW = Wmax(RomajiTok);
int posW    = Wmax(PosTok);

string PadOut(string s, int n) => s + new string(' ', Math.Max(0, n - s.Length));

var sb = new StringBuilder(kept.Count * 200);
sb.Append('[').Append('\n');
for (int i = 0; i < kept.Count; i++)
{
    var w = kept[i];
    sb.Append("  { ")
      .Append(PadOut(IdTok(w),     idW)).Append(", ")
      .Append(PadOut(KanjiTok(w),  kanjiW)).Append(", ")
      .Append(PadOut(KanaTok(w),   kanaW)).Append(", ")
      .Append(PadOut(RomajiTok(w), romajiW)).Append(", ")
      .Append("\"meanings\": ").Append(J(w.Meanings)).Append(", ")
      .Append(PadOut(PosTok(w),    posW)).Append(", ")
      .Append("\"tags\": ").Append(J(w.Tags)).Append(", ")
      .Append("\"frequencyRank\": ").Append(w.FrequencyRank)
      .Append(" }").Append(i < kept.Count - 1 ? "," : "").Append('\n');
}
sb.Append(']').Append('\n');

await File.WriteAllTextAsync(path, sb.ToString(), new UTF8Encoding(false));

Console.WriteLine($"Input  : {stats.Input}");
Console.WriteLine($"Kept   : {stats.Output}");
Console.WriteLine($"Dropped (duplicate id)     : {stats.DroppedDupId}");
Console.WriteLine($"Dropped (duplicate content): {stats.DroppedDupContent}");
Console.WriteLine($"Fixed frequencyRank        : {stats.FixedFreq}");
Console.WriteLine($"Normalised tag arrays      : {stats.NormalisedTags}");
return 0;

internal sealed class Stats
{
    public int Input, Output, DroppedDupId, DroppedDupContent, FixedFreq, NormalisedTags;
}

/// <summary>Local JP-shaped DTO for the cleaner. Decoupled from the runtime Word model
/// (which now stores forms positionally) because this tool only ever operates on the
/// JP vocabulary file's specific kanji/kana/romaji shape.</summary>
internal sealed class JpWord
{
    [JsonPropertyName("id")]            public string Id { get; set; } = string.Empty;
    [JsonPropertyName("kanji")]         public string Kanji { get; set; } = string.Empty;
    [JsonPropertyName("kana")]          public string Kana { get; set; } = string.Empty;
    [JsonPropertyName("romaji")]        public string Romaji { get; set; } = string.Empty;
    [JsonPropertyName("meanings")]      public List<string> Meanings { get; set; } = new();
    [JsonPropertyName("pos")]           public string PartOfSpeech { get; set; } = string.Empty;
    [JsonPropertyName("tags")]          public List<string> Tags { get; set; } = new();
    [JsonPropertyName("frequencyRank")] public int FrequencyRank { get; set; } = int.MaxValue;
    public string PrimaryMeaning => Meanings.Count > 0 ? Meanings[0] : string.Empty;
}
