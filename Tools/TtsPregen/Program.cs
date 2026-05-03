/// TTS Pre-generation Tool for LearnJP
///
/// Walks every language pack and every vocabulary entry, synthesises the target-language
/// audio via the Azure TTS REST API and writes the result to a local output directory.
///
/// Output files are named with the same SHA-256 scheme that FileTtsCache uses
/// (provider|lang|voice|text), so the generated files can be dropped directly into the
/// app's tts-cache folder on any device to skip network synthesis at runtime.
///
/// Audio formats (set via "outputFormat" in the config, no transcoding needed):
///   webm-24khz-16bit-mono-opus  (default) — WebM/Opus; best quality/size ratio, natively
///                                supported by Azure. Works on Android and Windows; NOT
///                                supported by iOS/macOS AVAudioPlayer — use mp3 for those.
///   audio-24khz-96kbitrate-mono-mp3        — MP3; universally supported on all platforms.
///
/// Any Azure TTS output format string is accepted; the file extension is derived
/// automatically (webm-* → .webm, ogg-* → .ogg, audio-*-mp3 / riff-* → .mp3/.wav, etc.).
///
/// Usage:
///   dotnet run [path/to/tts-pregen-config.json]
///
/// If no path is given, the tool looks for tts-pregen-config.json in the current directory,
/// then under Tools/TtsPregen/ relative to the solution root.

using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

// ── Config ─────────────────────────────────────────────────────────────────────────────────────

var configPath = args.Length > 0
    ? args[0]
    : FindConfig();

if (!File.Exists(configPath))
{
    Console.Error.WriteLine($"Config file not found: {configPath}");
    Console.Error.WriteLine("Copy tts-pregen-config.json from Tools/TtsPregen/ and fill in azureSpeechKey + azureSpeechRegion.");
    return 1;
}

var jsonOpts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
PregenConfig cfg;
try
{
    cfg = JsonSerializer.Deserialize<PregenConfig>(await File.ReadAllTextAsync(configPath), jsonOpts)
          ?? throw new InvalidDataException("Null result");
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Failed to parse config: {ex.Message}");
    return 1;
}

if (string.IsNullOrWhiteSpace(cfg.AzureSpeechKey))
{
    Console.Error.WriteLine("azureSpeechKey is not set in the config file.");
    return 1;
}
if (string.IsNullOrWhiteSpace(cfg.AzureSpeechRegion))
{
    Console.Error.WriteLine("azureSpeechRegion is not set in the config file.");
    return 1;
}

var outputFormat = string.IsNullOrWhiteSpace(cfg.OutputFormat)
    ? "webm-24khz-16bit-mono-opus"
    : cfg.OutputFormat.Trim();
var fileExtension = DeriveExtension(outputFormat);

Console.WriteLine($"Output format : {outputFormat} → {fileExtension}");

// ── Resolve Resources/Raw path ─────────────────────────────────────────────────────────────────

var rawRoot = string.IsNullOrWhiteSpace(cfg.ResourcesRawPath)
    ? FindResourcesRaw(configPath)
    : Path.GetFullPath(cfg.ResourcesRawPath, Path.GetDirectoryName(configPath)!);

if (rawRoot is null || !Directory.Exists(rawRoot))
{
    Console.Error.WriteLine("Cannot locate Resources/Raw directory. Set 'resourcesRawPath' in the config.");
    return 1;
}

Console.WriteLine($"Resources/Raw : {rawRoot}");

// ── Load language packs ────────────────────────────────────────────────────────────────────────

var indexPath = Path.Combine(rawRoot, "Languages", "index.json");
if (!File.Exists(indexPath))
{
    Console.Error.WriteLine($"Language index not found: {indexPath}");
    return 1;
}

PackIndex? index;
try { index = JsonSerializer.Deserialize<PackIndex>(await File.ReadAllTextAsync(indexPath), jsonOpts); }
catch (Exception ex) { Console.Error.WriteLine($"Failed to parse language index: {ex.Message}"); return 1; }

if (index is null || index.Packs.Count == 0)
{
    Console.Error.WriteLine("No language packs found in index.");
    return 1;
}

var packs = new List<(LanguagePack pack, List<VocabEntry> words)>();
foreach (var packFile in index.Packs)
{
    var packPath = Path.Combine(rawRoot, "Languages", packFile);
    if (!File.Exists(packPath)) { Console.WriteLine($"[WARN] pack file missing: {packFile}"); continue; }

    LanguagePack? pack;
    try { pack = JsonSerializer.Deserialize<LanguagePack>(await File.ReadAllTextAsync(packPath), jsonOpts); }
    catch (Exception ex) { Console.WriteLine($"[WARN] failed to parse {packFile}: {ex.Message}"); continue; }

    if (pack is null || string.IsNullOrWhiteSpace(pack.Id)) { Console.WriteLine($"[WARN] invalid pack: {packFile}"); continue; }
    if (string.IsNullOrWhiteSpace(pack.TtsLocale)) { Console.WriteLine($"[SKIP] {pack.Id}: no ttsLocale"); continue; }
    if (!pack.ProviderVoices.TryGetValue("azure", out var voice) || string.IsNullOrWhiteSpace(voice))
    {
        Console.WriteLine($"[SKIP] {pack.Id}: no azure voice configured");
        continue;
    }

    var vocabPath = Path.Combine(rawRoot, pack.VocabFile);
    if (!File.Exists(vocabPath)) { Console.WriteLine($"[WARN] vocab file missing: {pack.VocabFile}"); continue; }

    List<VocabEntry>? words;
    try { words = JsonSerializer.Deserialize<List<VocabEntry>>(await File.ReadAllTextAsync(vocabPath), jsonOpts); }
    catch (Exception ex) { Console.WriteLine($"[WARN] failed to parse vocab {pack.VocabFile}: {ex.Message}"); continue; }

    words ??= new();
    packs.Add((pack, words));
    Console.WriteLine($"Loaded pack: {pack.Id} ({pack.DisplayName}) — {words.Count} words");
}

if (packs.Count == 0) { Console.Error.WriteLine("No usable packs found."); return 1; }

// ── Prepare output directory ───────────────────────────────────────────────────────────────────

var outputDir = Path.GetFullPath(
    string.IsNullOrWhiteSpace(cfg.OutputDirectory) ? "./tts-output" : cfg.OutputDirectory,
    Path.GetDirectoryName(configPath)!);
Directory.CreateDirectory(outputDir);
Console.WriteLine($"Output dir    : {outputDir}");
Console.WriteLine();

// ── Synthesis loop ─────────────────────────────────────────────────────────────────────────────

var totalGenerated = 0;
var totalSkipped   = 0;
var totalFailed    = 0;

using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

var concurrency = Math.Max(1, cfg.MaxConcurrency);
var delay       = Math.Max(0, cfg.DelayBetweenRequestsMs);

foreach (var (pack, words) in packs)
{
    var voice  = pack.ProviderVoices["azure"];
    var locale = pack.TtsLocale;
    var isJapanese = string.Equals(pack.Id, "ja", StringComparison.OrdinalIgnoreCase)
                     || pack.BehaviorType == "Japanese";

    // Determine form indices from the pack declaration.
    var formKeys = pack.Forms.Count > 0 ? pack.Forms : new List<string> { "kanji" };
    var kanjiIdx  = formKeys.IndexOf("kanji");
    var kanaIdx   = formKeys.IndexOf("kana");

    Console.WriteLine($"=== {pack.Id} | {locale} | {voice} ===");

    // Deduplicate by tts-text within this pack to avoid re-synthesising the same sound.
    var seenTexts = new HashSet<string>(StringComparer.Ordinal);

    var semaphore = new SemaphoreSlim(concurrency, concurrency);
    var tasks = new List<Task>();

    foreach (var word in words)
    {
        if (string.IsNullOrWhiteSpace(word.Id)) continue;

        // Resolve TTS text: for Japanese packs use kanji (or kana fallback) because that
        // produces the most accurate pronunciation from Azure TTS. For all other languages
        // use the primary form (forms[0]).
        string ttsText;
        if (isJapanese)
        {
            var kanji = kanjiIdx >= 0 ? word.FormAt(kanjiIdx, formKeys) : string.Empty;
            var kana  = kanaIdx  >= 0 ? word.FormAt(kanaIdx,  formKeys) : string.Empty;
            ttsText = string.IsNullOrWhiteSpace(kanji) ? kana : kanji;
        }
        else
        {
            ttsText = word.FormAt(0, formKeys);
        }

        if (string.IsNullOrWhiteSpace(ttsText)) continue;
        if (!seenTexts.Add(ttsText)) { totalSkipped++; continue; }

        // Compute the cache key the app uses so the files are drop-in replacements.
        var cacheKey  = $"azure|{locale}|{voice}|{ttsText}";
        var fileName  = CacheFileName(cacheKey, fileExtension);
        var filePath  = Path.Combine(outputDir, fileName);

        if (!cfg.Overwrite && File.Exists(filePath))
        {
            totalSkipped++;
            continue;
        }

        var capturedText  = ttsText;
        var capturedPath  = filePath;

        await semaphore.WaitAsync();
        tasks.Add(Task.Run(async () =>
        {
            try
            {
                var audio = await SynthesizeAsync(http, cfg, outputFormat, locale, voice, capturedText);
                if (audio is null || audio.Length < 64)
                {
                    Console.WriteLine($"  [FAIL] {word.Id}: synthesis returned no data");
                    Interlocked.Increment(ref totalFailed);
                    return;
                }
                await File.WriteAllBytesAsync(capturedPath, audio);
                Console.WriteLine($"  [OK] {word.Id}: {capturedText} → {Path.GetFileName(capturedPath)} ({audio.Length / 1024}KB)");
                Interlocked.Increment(ref totalGenerated);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  [FAIL] {word.Id}: {ex.Message}");
                Interlocked.Increment(ref totalFailed);
            }
            finally
            {
                semaphore.Release();
                if (delay > 0) await Task.Delay(delay);
            }
        }));
    }

    await Task.WhenAll(tasks);
    Console.WriteLine();
}

Console.WriteLine($"Done. Generated: {totalGenerated}  Skipped: {totalSkipped}  Failed: {totalFailed}");
Console.WriteLine($"Output: {outputDir}");
Console.WriteLine();
Console.WriteLine($"To pre-populate the app cache, copy the {fileExtension} files to the device's");
Console.WriteLine("  AppData/LearnJP/tts-cache/  directory.");
return totalFailed > 0 ? 2 : 0;

// ── Helpers ────────────────────────────────────────────────────────────────────────────────────

static string CacheFileName(string cacheKey, string extension)
{
    var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(cacheKey));
    return Convert.ToHexString(bytes) + extension;
}

/// <summary>Derives a file extension from an Azure TTS output-format string.
/// Examples: "webm-24khz-16bit-mono-opus" → ".webm",
///           "ogg-16khz-16bit-mono-opus"  → ".ogg",
///           "audio-24khz-96kbitrate-mono-mp3" → ".mp3",
///           "riff-24khz-16bit-mono-pcm"  → ".wav"</summary>
static string DeriveExtension(string format)
{
    var f = format.ToLowerInvariant();
    if (f.StartsWith("webm"))  return ".webm";
    if (f.StartsWith("ogg"))   return ".ogg";
    if (f.EndsWith("mp3"))     return ".mp3";
    if (f.StartsWith("riff"))  return ".wav";
    return ".bin";
}

static async Task<byte[]?> SynthesizeAsync(
    HttpClient http, PregenConfig cfg, string outputFormat, string locale, string voice, string text)
{
    var endpoint = $"https://{cfg.AzureSpeechRegion}.tts.speech.microsoft.com/cognitiveservices/v1";
    var safeText = System.Security.SecurityElement.Escape(text) ?? text;
    var ssml =
        $"<speak version='1.0' xml:lang='{locale}'>" +
          $"<voice xml:lang='{locale}' name='{voice}'>{safeText}</voice>" +
        "</speak>";

    using var req = new HttpRequestMessage(HttpMethod.Post, endpoint);
    req.Headers.Add("Ocp-Apim-Subscription-Key", cfg.AzureSpeechKey);
    req.Headers.Add("X-Microsoft-OutputFormat", outputFormat);
    req.Headers.Add("User-Agent", "LearnJP-TtsPregen/1.0");
    req.Content = new StringContent(ssml, Encoding.UTF8, "application/ssml+xml");

    using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseContentRead);
    if (!resp.IsSuccessStatusCode)
    {
        var body = await TryReadBodyAsync(resp);
        throw new HttpRequestException($"HTTP {(int)resp.StatusCode}: {body}");
    }
    return await resp.Content.ReadAsByteArrayAsync();
}

static async Task<string> TryReadBodyAsync(HttpResponseMessage resp)
{
    try { return await resp.Content.ReadAsStringAsync(); }
    catch { return "<unreadable>"; }
}

static string? FindConfig()
{
    // 1. Current working directory
    var local = Path.Combine(Directory.GetCurrentDirectory(), "tts-pregen-config.json");
    if (File.Exists(local)) return local;

    // 2. Walk up to find the solution root (contains *.sln) then look in Tools/TtsPregen
    var dir = AppContext.BaseDirectory;
    for (int i = 0; i < 8; i++)
    {
        if (Directory.GetFiles(dir, "*.sln").Length > 0)
        {
            var candidate = Path.Combine(dir, "Tools", "TtsPregen", "tts-pregen-config.json");
            if (File.Exists(candidate)) return candidate;
        }
        var parent = Directory.GetParent(dir);
        if (parent is null) break;
        dir = parent.FullName;
    }

    return local; // will be reported as missing
}

static string? FindResourcesRaw(string configPath)
{
    // Try relative to config file location first.
    var baseDir = Path.GetDirectoryName(configPath) ?? Directory.GetCurrentDirectory();

    // Walk up looking for Resources/Raw.
    var dir = baseDir;
    for (int i = 0; i < 8; i++)
    {
        var candidate = Path.Combine(dir, "Resources", "Raw");
        if (Directory.Exists(candidate)) return candidate;
        var parent = Directory.GetParent(dir);
        if (parent is null) break;
        dir = parent.FullName;
    }
    return null;
}

// ── DTOs ───────────────────────────────────────────────────────────────────────────────────────

internal sealed class PregenConfig
{
    [JsonPropertyName("azureSpeechKey")]         public string AzureSpeechKey         { get; set; } = string.Empty;
    [JsonPropertyName("azureSpeechRegion")]      public string AzureSpeechRegion      { get; set; } = "eastus";
    /// <summary>Path to Resources/Raw. Leave empty to auto-detect from the directory tree.</summary>
    [JsonPropertyName("resourcesRawPath")]       public string ResourcesRawPath       { get; set; } = string.Empty;
    [JsonPropertyName("outputDirectory")]        public string OutputDirectory        { get; set; } = "./tts-output";
    /// <summary>
    /// Azure TTS output-format string. The file extension is derived automatically.
    /// Recommended options (no transcoding — Azure outputs these natively):
    ///   webm-24khz-16bit-mono-opus         (default) best quality/size; Android + Windows only
    ///   audio-24khz-96kbitrate-mono-mp3    universally supported including iOS/macOS
    ///   ogg-24khz-16bit-mono-opus          OGG/Opus; Android + Windows
    /// Leave empty to use the default (webm-24khz-16bit-mono-opus).
    /// </summary>
    [JsonPropertyName("outputFormat")]           public string OutputFormat           { get; set; } = string.Empty;
    [JsonPropertyName("maxConcurrency")]         public int    MaxConcurrency         { get; set; } = 4;
    [JsonPropertyName("overwrite")]              public bool   Overwrite              { get; set; } = false;
    [JsonPropertyName("delayBetweenRequestsMs")] public int    DelayBetweenRequestsMs { get; set; } = 50;
}

internal sealed class PackIndex
{
    [JsonPropertyName("packs")] public List<string> Packs { get; set; } = new();
}

internal sealed class LanguagePack
{
    [JsonPropertyName("id")]             public string              Id             { get; set; } = string.Empty;
    [JsonPropertyName("displayName")]    public string              DisplayName    { get; set; } = string.Empty;
    [JsonPropertyName("ttsLocale")]      public string              TtsLocale      { get; set; } = string.Empty;
    [JsonPropertyName("providerVoices")] public Dictionary<string, string> ProviderVoices { get; set; } = new();
    [JsonPropertyName("forms")]          public List<string>        Forms          { get; set; } = new();
    [JsonPropertyName("vocabFile")]      public string              VocabFile      { get; set; } = "vocabulary.json";
    [JsonPropertyName("behaviorType")]   public string              BehaviorType   { get; set; } = "Generic";
}

/// <summary>Generic vocabulary entry DTO that reads only the fields needed for TTS pre-generation.
/// Form values are stored in a raw JsonElement so the tool works for any language pack without
/// being hard-coded to JP-specific property names.</summary>
internal sealed class VocabEntry
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    // Catch-all for all remaining JSON properties so we can read form values by key at runtime.
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }

    /// <summary>Returns the value of the form field at <paramref name="index"/> in
    /// <paramref name="formKeys"/>, or empty string if not present.</summary>
    public string FormAt(int index, IList<string> formKeys)
    {
        if (index < 0 || index >= formKeys.Count) return string.Empty;
        var key = formKeys[index];
        if (Extra is not null && Extra.TryGetValue(key, out var el) && el.ValueKind == JsonValueKind.String)
            return el.GetString() ?? string.Empty;
        return string.Empty;
    }
}
