using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

// ── Resolve working directory ────────────────────────────────────────────────
string rawDir;
if (args.Length > 0)
{
    var a = args[0];
    rawDir = Directory.Exists(a) ? a : Path.GetDirectoryName(Path.GetFullPath(a))!;
}
else
{
    rawDir = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "..", "Resources", "Raw"));
}

if (!Directory.Exists(rawDir))
{
    Console.Error.WriteLine($"Directory not found: {rawDir}");
    return 1;
}

var jsonOpts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

// ── Canonical tag synonyms ────────────────────────────────────────────────────
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

List<string> NormaliseTagsById(IEnumerable<string>? tags, string id)
{
    var set = new SortedSet<string>(StringComparer.Ordinal);
    foreach (var t in tags ?? Enumerable.Empty<string>())
    {
        if (string.IsNullOrWhiteSpace(t)) continue;
        set.Add(Canon(t));
    }
    if (id.StartsWith("n5-", StringComparison.Ordinal)) set.Add("n5");
    if (id.StartsWith("h-",  StringComparison.Ordinal)) set.Add("hiragana");
    if (id.StartsWith("k-",  StringComparison.Ordinal)) set.Add("katakana");
    return set.ToList();
}

// ── Levenshtein distance (for close-match detection) ─────────────────────────
static int Levenshtein(string a, string b)
{
    if (a.Length == 0) return b.Length;
    if (b.Length == 0) return a.Length;
    var d = new int[a.Length + 1, b.Length + 1];
    for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
    for (int j = 0; j <= b.Length; j++) d[0, j] = j;
    for (int i = 1; i <= a.Length; i++)
        for (int j = 1; j <= b.Length; j++)
            d[i, j] = Math.Min(
                Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                d[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
    return d[a.Length, b.Length];
}

// ── Discover vocabulary files ─────────────────────────────────────────────────
// Source files: vocabulary_XX.json (no hyphen in the language code part)
// Translation files: vocabulary_XX-YY.json  (e.g. vocabulary_lat-en.json)
var translationPattern = new Regex(@"^vocabulary_[^-]+-[a-z]{2}\.json$", RegexOptions.IgnoreCase);
var sourceFiles      = Directory.GetFiles(rawDir, "vocabulary_*.json")
    .Where(f => !translationPattern.IsMatch(Path.GetFileName(f)))
    .OrderBy(f => f).ToList();
var translationFiles = Directory.GetFiles(rawDir, "vocabulary_*-*.json")
    .Where(f =>  translationPattern.IsMatch(Path.GetFileName(f)))
    .OrderBy(f => f).ToList();

// ── Process each source vocabulary file ──────────────────────────────────────
foreach (var sourceFile in sourceFiles)
{
    var fileName = Path.GetFileName(sourceFile);
    var langCode = Regex.Match(fileName, @"vocabulary_(.+)\.json").Groups[1].Value;
    Console.WriteLine($"\n── {fileName} ──────────────────────────────────────────");

    var raw     = await File.ReadAllTextAsync(sourceFile);
    var isJp    = langCode == "ja";

    if (isJp)
    {
        // JP format: kanji / kana / romaji
        var entries = JsonSerializer.Deserialize<List<JpWord>>(raw, jsonOpts) ?? new();
        var stats   = new Stats { Input = entries.Count };

        string Fingerprint(JpWord w) =>
            $"{(w.Kana ?? "").Trim()}|{(w.Romaji ?? "").Trim().ToLowerInvariant()}";

        var seenIds      = new HashSet<string>(StringComparer.Ordinal);
        var byFp         = new Dictionary<string, JpWord>(StringComparer.Ordinal);
        var kept         = new List<JpWord>(entries.Count);

        foreach (var src in entries)
        {
            src.Id           = (src.Id           ?? "").Trim();
            src.Kanji        = (src.Kanji        ?? "").Trim();
            src.Kana         = (src.Kana         ?? "").Trim();
            src.Romaji       = (src.Romaji       ?? "").Trim();
            src.PartOfSpeech = (src.PartOfSpeech ?? "").Trim();

            if (string.IsNullOrWhiteSpace(src.Id) || !seenIds.Add(src.Id))
            { stats.DroppedDupId++; continue; }

            var beforeTags = string.Join('|', src.Tags ?? new());
            src.Tags = NormaliseTagsById(src.Tags, src.Id);
            if (string.Join('|', src.Tags) != beforeTags) stats.NormalisedTags++;

            if (src.FrequencyRank < 0 || src.FrequencyRank == int.MaxValue)
            { src.FrequencyRank = src.Id.StartsWith("n5-") ? 9999 : 0; stats.FixedFreq++; }

            var fp = Fingerprint(src);
            if (byFp.TryGetValue(fp, out var keeper))
            {
                var merged = new SortedSet<string>(keeper.Tags, StringComparer.Ordinal);
                foreach (var t in src.Tags) merged.Add(t);
                keeper.Tags = merged.ToList();
                stats.DroppedDupContent++;
                continue;
            }
            byFp[fp] = src;
            kept.Add(src);
        }
        stats.Output = kept.Count;

        int Group(JpWord w) => w.Id switch
        {
            var s when s.StartsWith("h-") => 0,
            var s when s.StartsWith("k-") => 1,
            _                             => 2
        };
        kept.Sort((a, b) =>
        {
            var g = Group(a) - Group(b); if (g != 0) return g;
            return string.Compare(a.Id, b.Id, StringComparison.Ordinal);
        });

        string J(object? v) => JsonSerializer.Serialize(v);
        string IdTok(JpWord w)     => $"\"id\": \"{w.Id}\"";
        string KanjiTok(JpWord w)  => $"\"kanji\": \"{w.Kanji}\"";
        string KanaTok(JpWord w)   => $"\"kana\": \"{w.Kana}\"";
        string RomajiTok(JpWord w) => $"\"romaji\": \"{w.Romaji}\"";
        string PosTok(JpWord w)    => $"\"pos\": \"{w.PartOfSpeech}\"";
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
              .Append(PadOut(PosTok(w),    posW)).Append(", ")
              .Append("\"tags\": ").Append(J(w.Tags)).Append(", ")
              .Append("\"frequencyRank\": ").Append(w.FrequencyRank)
              .Append(" }").Append(i < kept.Count - 1 ? "," : "").Append('\n');
        }
        sb.Append(']').Append('\n');
        await File.WriteAllTextAsync(sourceFile, sb.ToString(), new UTF8Encoding(false));
        PrintStats(stats);
    }
    else
    {
        // Generic format: single "word" field (Latin, Italian, …)
        var entries = JsonSerializer.Deserialize<List<GenericWord>>(raw, jsonOpts) ?? new();
        var stats   = new Stats { Input = entries.Count };

        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        var byFp    = new Dictionary<string, GenericWord>(StringComparer.Ordinal);
        var kept    = new List<GenericWord>(entries.Count);

        foreach (var src in entries)
        {
            src.Id           = (src.Id           ?? "").Trim();
            src.Word         = (src.Word         ?? "").Trim();
            src.PartOfSpeech = (src.PartOfSpeech ?? "").Trim();

            if (string.IsNullOrWhiteSpace(src.Id) || !seenIds.Add(src.Id))
            { stats.DroppedDupId++; continue; }

            var beforeTags = string.Join('|', src.Tags ?? new());
            src.Tags = NormaliseTagsById(src.Tags, src.Id);
            if (string.Join('|', src.Tags) != beforeTags) stats.NormalisedTags++;

            if (src.FrequencyRank < 0 || src.FrequencyRank == int.MaxValue)
            { src.FrequencyRank = 0; stats.FixedFreq++; }

            var fp = $"{src.Word.ToLowerInvariant()}|{src.PartOfSpeech.ToLowerInvariant()}";
            if (byFp.TryGetValue(fp, out var keeper))
            {
                var merged = new SortedSet<string>(keeper.Tags, StringComparer.Ordinal);
                foreach (var t in src.Tags) merged.Add(t);
                keeper.Tags = merged.ToList();
                stats.DroppedDupContent++;
                continue;
            }
            byFp[fp] = src;
            kept.Add(src);
        }
        stats.Output = kept.Count;

        kept.Sort((a, b) => string.Compare(a.Id, b.Id, StringComparison.Ordinal));

        string J(object? v) => JsonSerializer.Serialize(v);
        string IdTok(GenericWord w)   => $"\"id\": \"{w.Id}\"";
        string WordTok(GenericWord w) => $"\"word\": \"{w.Word}\"";
        string PosTok(GenericWord w)  => $"\"pos\": \"{w.PartOfSpeech}\"";
        int Wmax(Func<GenericWord, string> sel) => kept.Count > 0 ? kept.Max(w => sel(w).Length) : 0;
        int idW   = Wmax(IdTok);
        int wordW = Wmax(WordTok);
        int posW  = Wmax(PosTok);
        string PadOut(string s, int n) => s + new string(' ', Math.Max(0, n - s.Length));

        var sb = new StringBuilder(kept.Count * 120);
        sb.Append('[').Append('\n');
        for (int i = 0; i < kept.Count; i++)
        {
            var w = kept[i];
            sb.Append("  { ")
              .Append(PadOut(IdTok(w),   idW)).Append(", ")
              .Append(PadOut(WordTok(w), wordW)).Append(", ")
              .Append(PadOut(PosTok(w),  posW)).Append(", ")
              .Append("\"tags\": ").Append(J(w.Tags)).Append(", ")
              .Append("\"frequencyRank\": ").Append(w.FrequencyRank)
              .Append(" }").Append(i < kept.Count - 1 ? "," : "").Append('\n');
        }
        sb.Append(']').Append('\n');
        await File.WriteAllTextAsync(sourceFile, sb.ToString(), new UTF8Encoding(false));
        PrintStats(stats);
    }
}

// ── Check translation files for meaning collisions ───────────────────────────
int totalExact = 0;
int totalClose = 0;

foreach (var transFile in translationFiles)
{
    var fileName = Path.GetFileName(transFile);
    Console.WriteLine($"\n── {fileName} ──────────────────────────────────────────");

    // Load the corresponding source file to resolve IDs → display words
    var match     = Regex.Match(fileName, @"vocabulary_(.+)-([a-z]{2})\.json");
    var srcLang   = match.Groups[1].Value;
    var transLang = match.Groups[2].Value;
    var srcFile   = Path.Combine(rawDir, $"vocabulary_{srcLang}.json");

    // Build id → display-label map
    var idToLabel = new Dictionary<string, string>(StringComparer.Ordinal);
    if (File.Exists(srcFile))
    {
        var srcRaw = await File.ReadAllTextAsync(srcFile);
        if (srcLang == "ja")
        {
            var words = JsonSerializer.Deserialize<List<JpWord>>(srcRaw, jsonOpts) ?? new();
            foreach (var w in words)
            {
                var label = string.IsNullOrWhiteSpace(w.Kanji) ? w.Kana : w.Kanji;
                idToLabel[w.Id] = $"{label} ({w.Romaji})";
            }
        }
        else
        {
            var words = JsonSerializer.Deserialize<List<GenericWord>>(srcRaw, jsonOpts) ?? new();
            foreach (var w in words) idToLabel[w.Id] = w.Word;
        }
    }

    string Label(string id) => idToLabel.TryGetValue(id, out var l) ? $"{id} [{l}]" : id;

    var transRaw  = await File.ReadAllTextAsync(transFile);
    var entries   = JsonSerializer.Deserialize<List<TranslationEntry>>(transRaw, jsonOpts) ?? new();

    // Build primaryMeaning → list-of-ids map
    var byPrimary = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
    foreach (var e in entries)
    {
        if (e.Meanings is null || e.Meanings.Count == 0) continue;
        var pm = e.Meanings[0].Trim();
        if (string.IsNullOrEmpty(pm)) continue;
        if (!byPrimary.TryGetValue(pm, out var list)) byPrimary[pm] = list = new();
        list.Add(e.Id);
    }

    // Exact collisions: same primary meaning for ≥ 2 different source words
    var exactCollisions = byPrimary
        .Where(kv => kv.Value.Count > 1)
        .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
        .ToList();

    int fileExact = 0;
    foreach (var (meaning, ids) in exactCollisions)
    {
        Console.Error.WriteLine(
            $"  [COLLISION] \"{meaning}\" → {string.Join(", ", ids.Select(Label))}");
        fileExact++;
    }
    totalExact += fileExact;

    // Close matches — two flavours:
    //   1. One multi-word meaning is a prefix of another  (e.g. "to carry" ≈ "to carry away")
    //   2. Single-word meanings with length ≥ 6 that differ by exactly 1 edit
    //      (e.g. "boldness" ≈ "boldness" — unlikely but catches typos / plural variants)
    // Intentionally NOT flagging short-word noise like "wide" ≈ "wife".
    var primaries = byPrimary.Keys
        .OrderBy(m => m, StringComparer.OrdinalIgnoreCase)
        .ToList();

    bool IsPrefixPhrase(string shorter, string longer)
    {
        var ws = shorter.ToLowerInvariant()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var wl = longer.ToLowerInvariant()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (ws.Length == 0 || ws.Length >= wl.Length) return false;
        return ws.Zip(wl, (a2, b2) => a2 == b2).All(m => m);
    }

    bool IsCloseMatch(string a, string b)
    {
        // Prefix-phrase check (multi-word meanings only)
        if (IsPrefixPhrase(a, b) || IsPrefixPhrase(b, a)) return true;
        // Single-word near-miss (length ≥ 6, edit distance exactly 1)
        var wa = a.Split(' ');
        var wb = b.Split(' ');
        if (wa.Length == 1 && wb.Length == 1 && a.Length >= 6 && b.Length >= 6)
            return Levenshtein(a.ToLowerInvariant(), b.ToLowerInvariant()) == 1;
        return false;
    }

    int fileClose = 0;
    for (int i = 0; i < primaries.Count; i++)
    {
        for (int j = i + 1; j < primaries.Count; j++)
        {
            var a = primaries[i];
            var b = primaries[j];
            if (IsCloseMatch(a, b))
            {
                var idsA = string.Join(", ", byPrimary[a].Select(Label));
                var idsB = string.Join(", ", byPrimary[b].Select(Label));
                Console.WriteLine($"  [CLOSE]     \"{a}\" ({idsA})  ≈  \"{b}\" ({idsB})");
                fileClose++;
            }
        }
    }
    totalClose += fileClose;

    if (fileExact == 0 && fileClose == 0)
        Console.WriteLine("  No collisions or close matches found.");
    else
        Console.WriteLine($"  Exact collisions: {fileExact}  Close matches: {fileClose}");
}

if (translationFiles.Count > 0)
{
    Console.WriteLine($"\n══ Summary ═════════════════════════════════════════");
    Console.WriteLine($"  Total exact collisions : {totalExact}");
    Console.WriteLine($"  Total close matches    : {totalClose}");
}

return 0;

// ── Helpers ───────────────────────────────────────────────────────────────────
static void PrintStats(Stats s)
{
    Console.WriteLine($"  Input  : {s.Input}");
    Console.WriteLine($"  Kept   : {s.Output}");
    Console.WriteLine($"  Dropped (duplicate id)     : {s.DroppedDupId}");
    Console.WriteLine($"  Dropped (duplicate content): {s.DroppedDupContent}");
    Console.WriteLine($"  Fixed frequencyRank        : {s.FixedFreq}");
    Console.WriteLine($"  Normalised tag arrays      : {s.NormalisedTags}");
}

// ── DTOs ──────────────────────────────────────────────────────────────────────

internal sealed class Stats
{
    public int Input, Output, DroppedDupId, DroppedDupContent, FixedFreq, NormalisedTags;
}

/// <summary>JP-shaped DTO — kanji / kana / romaji form, no embedded meanings
/// (meanings live in the companion vocabulary_ja-XX.json translation files).</summary>
internal sealed class JpWord
{
    [JsonPropertyName("id")]            public string Id { get; set; } = string.Empty;
    [JsonPropertyName("kanji")]         public string Kanji { get; set; } = string.Empty;
    [JsonPropertyName("kana")]          public string Kana { get; set; } = string.Empty;
    [JsonPropertyName("romaji")]        public string Romaji { get; set; } = string.Empty;
    [JsonPropertyName("pos")]           public string PartOfSpeech { get; set; } = string.Empty;
    [JsonPropertyName("tags")]          public List<string> Tags { get; set; } = new();
    [JsonPropertyName("frequencyRank")] public int FrequencyRank { get; set; } = int.MaxValue;
}

/// <summary>Generic single-word DTO for non-JP languages (Latin, Italian, …)
/// where the source file has only one word form.</summary>
internal sealed class GenericWord
{
    [JsonPropertyName("id")]            public string Id { get; set; } = string.Empty;
    [JsonPropertyName("word")]          public string Word { get; set; } = string.Empty;
    [JsonPropertyName("pos")]           public string PartOfSpeech { get; set; } = string.Empty;
    [JsonPropertyName("tags")]          public List<string> Tags { get; set; } = new();
    [JsonPropertyName("frequencyRank")] public int FrequencyRank { get; set; } = int.MaxValue;
}

/// <summary>Translation entry — shared by all XX-YY.json translation files.</summary>
internal sealed class TranslationEntry
{
    [JsonPropertyName("id")]       public string Id { get; set; } = string.Empty;
    [JsonPropertyName("meanings")] public List<string> Meanings { get; set; } = new();
}
