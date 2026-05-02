using LearnJP.Models;
using LearnJP.Services;
using LearnJP.Tools.StrategySim;

// Default vocabulary path: ../../Resources/Raw/vocabulary.json relative to the project.
var defaults = new RunConfig
{
    VocabPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "Resources", "Raw", "vocabulary.json")),
    Bot = "learner",
    Strategy = LearningStrategy.Spaced,
    Turns = 2000,
    Limit = null,
    ChanceP = 0.5,
    Seed = 1234,
    CsvOut = null,
    All = false
};

var cfg = ParseArgs(args, defaults);

if (!File.Exists(cfg.VocabPath))
{
    Console.Error.WriteLine($"vocabulary.json not found at: {cfg.VocabPath}");
    Console.Error.WriteLine("Pass --vocab <path> to override.");
    return 1;
}

if (cfg.All)
{
    foreach (var s in new[] { LearningStrategy.Neutral, LearningStrategy.Spaced })
    foreach (var b in BuildAllBots())
        await RunOne(cfg with { Strategy = s, Bot = b });
}
else
{
    await RunOne(cfg);
}
return 0;

static IEnumerable<string> BuildAllBots() => new[] { "random", "always-right", "always-wrong", "chance-0.25", "chance-0.50", "chance-0.75", "learner" };

static bool IsKana(Word w) =>
    w.Id.StartsWith("h-", StringComparison.Ordinal) ||
    w.Id.StartsWith("k-", StringComparison.Ordinal) ||
    w.Tags.Contains("hiragana") ||
    w.Tags.Contains("katakana");

static async Task RunOne(RunConfig cfg)
{
    var rng = new Random(cfg.Seed);
    var vocab = new MemoryVocabularyService(cfg.VocabPath, cfg.Limit);
    var store = new MemoryProficiencyStore();
    var settings = new MemorySettingsService { SelectedLearningStrategy = cfg.Strategy };
    var gen = new QuestionGenerator(vocab, store, settings);

    await vocab.EnsureLoadedAsync();
    var bot = ResolveBot(cfg.Bot, id => store.Get(id).Overall);
    var analyzer = new Analyzer();

    for (int turn = 0; turn < cfg.Turns; turn++)
    {
        var q = await gen.NextAsync(cfg.Strategy);
        if (q is null) break;
        var pickIdx = bot.PickOptionIndex(q, rng);
        var correct = q.Options[pickIdx].IsCorrect;
        await store.RecordAsync(q.Target.Id, q.Criterion, correct);
        analyzer.Record(new TurnRecord
        {
            Turn = turn,
            WordId = q.Target.Id,
            Criterion = q.Criterion,
            Correct = correct,
            OverallAfter = store.Get(q.Target.Id).Overall,
            FrontierSize = gen.CurrentNewTermFrontier.Count
        });
    }

    // The generator excludes kana entries by default (no include filter for hiragana/katakana),
    // so the analyzer should report against the *eligible* pool only.
    var eligible = vocab.All.Where(w => !IsKana(w)).ToList();
    analyzer.Print(store, eligible, bot.Name, cfg.Strategy);
    if (cfg.CsvOut is { } path)
    {
        var fileName = Path.GetFileNameWithoutExtension(path) + "-" + bot.Name + "-" + cfg.Strategy + Path.GetExtension(path);
        var full = Path.Combine(Path.GetDirectoryName(path) ?? ".", fileName);
        analyzer.DumpCsv(full);
        Console.WriteLine($"  csv               : {full}");
    }
}

static IAnswerBot ResolveBot(string name, Func<string, double> proficiencyOf)
{
    if (name.StartsWith("chance-", StringComparison.OrdinalIgnoreCase))
    {
        var p = double.Parse(name["chance-".Length..], System.Globalization.CultureInfo.InvariantCulture);
        return new ChanceBot(p);
    }
    return name.ToLowerInvariant() switch
    {
        "random"        => new RandomBot(),
        "always-right"  => new AlwaysRightBot(),
        "always-wrong"  => new AlwaysWrongBot(),
        "learner"       => new LearnerBot(proficiencyOf),
        _               => throw new ArgumentException($"Unknown bot: {name}")
    };
}

static RunConfig ParseArgs(string[] args, RunConfig def)
{
    var cfg = def;
    for (int i = 0; i < args.Length; i++)
    {
        var a = args[i];
        string Next() { i++; return args[i]; }
        switch (a)
        {
            case "--vocab":     cfg = cfg with { VocabPath = Next() }; break;
            case "--bot":       cfg = cfg with { Bot = Next() }; break;
            case "--strategy":  cfg = cfg with { Strategy = Enum.Parse<LearningStrategy>(Next(), ignoreCase: true) }; break;
            case "--turns":     cfg = cfg with { Turns = int.Parse(Next()) }; break;
            case "--limit":     cfg = cfg with { Limit = int.Parse(Next()) }; break;
            case "--chance-p":  cfg = cfg with { ChanceP = double.Parse(Next(), System.Globalization.CultureInfo.InvariantCulture) }; break;
            case "--seed":      cfg = cfg with { Seed = int.Parse(Next()) }; break;
            case "--csv":       cfg = cfg with { CsvOut = Next() }; break;
            case "--all":       cfg = cfg with { All = true }; break;
            case "--help":
            case "-h":
                Console.WriteLine("StrategySim — exercises QuestionGenerator with synthetic answer bots.");
                Console.WriteLine();
                Console.WriteLine("Options:");
                Console.WriteLine("  --vocab PATH      Path to vocabulary.json (default: project Resources/Raw)");
                Console.WriteLine("  --bot NAME        random|always-right|always-wrong|learner|chance-0.50  (default: learner)");
                Console.WriteLine("  --strategy NAME   Neutral|Spaced|QuickReview|WeakFocus  (default: Spaced)");
                Console.WriteLine("  --turns N         Question count (default: 2000)");
                Console.WriteLine("  --limit N         Cap pool to first N words (default: full)");
                Console.WriteLine("  --seed N          RNG seed for repeatability (default: 1234)");
                Console.WriteLine("  --csv PATH        Write per-turn dump to PATH (filename gets bot+strategy suffix)");
                Console.WriteLine("  --all             Run every bot × every strategy and print all summaries");
                Environment.Exit(0);
                break;
        }
    }
    return cfg;
}

internal record RunConfig
{
    public required string VocabPath { get; init; }
    public required string Bot { get; init; }
    public required LearningStrategy Strategy { get; init; }
    public required int Turns { get; init; }
    public required int? Limit { get; init; }
    public required double ChanceP { get; init; }
    public required int Seed { get; init; }
    public required string? CsvOut { get; init; }
    public required bool All { get; init; }
}
