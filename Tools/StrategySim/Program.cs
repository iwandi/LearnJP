using LearnJP.Models;
using LearnJP.Services;
using LearnJP.Tools.StrategySim;

// Sweep value sets — exposed at the top so they're easy to tweak.
int[] FrontierSweep = { 6, 12, 24, 48 };
int[] IntervalCapSweep = { 60, 120, 250, 500 };

// Default vocabulary path: ../../Resources/Raw/vocabulary.json relative to the project.
var defaults = new RunConfig
{
    VocabPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "Resources", "Raw", "vocabulary.json")),
    Bot = "learner",
    Strategy = LearningStrategy.Fsrs,
    // ~35 picks × 500-word pool ≈ 17,500. At 100 answers/day that's ~6 months of practice.
    Turns = 17500,
    Limit = null,
    ChanceP = 0.5,
    Seed = 1234,
    CsvOut = null,
    All = false,
    Frontier = 12,
    IntervalCap = 250,
    Sweep = null
};

var cfg = ParseArgs(args, defaults);

if (!File.Exists(cfg.VocabPath))
{
    Console.Error.WriteLine($"vocabulary.json not found at: {cfg.VocabPath}");
    Console.Error.WriteLine("Pass --vocab <path> to override.");
    return 1;
}

if (cfg.Sweep is { } sweep)
{
    var runs = BuildSweep(cfg, sweep, FrontierSweep, IntervalCapSweep).ToList();
    await RunInParallel(runs);
}
else if (cfg.All)
{
    var runs = (
        from s in new[] { LearningStrategy.Neutral, LearningStrategy.Spaced, LearningStrategy.Fsrs }
        from b in BuildAllBots()
        select cfg with { Strategy = s, Bot = b }
    ).ToList();
    await RunInParallel(runs);
}
else
{
    Console.Write(await RunOne(cfg));
}
return 0;

static async Task RunInParallel(IList<RunConfig> runs)
{
    // Cap to processor count so a 30-run sweep doesn't oversubscribe; preserves input order
    // when printing so the output stays diff-friendly across re-runs.
    var degree = Math.Max(1, Environment.ProcessorCount);
    var sem = new SemaphoreSlim(degree, degree);
    var outputs = new string[runs.Count];
    var tasks = new Task[runs.Count];
    for (int i = 0; i < runs.Count; i++)
    {
        var idx = i;
        var run = runs[i];
        tasks[i] = Task.Run(async () =>
        {
            await sem.WaitAsync();
            try { outputs[idx] = await RunOne(run); }
            finally { sem.Release(); }
        });
    }
    await Task.WhenAll(tasks);
    foreach (var o in outputs) Console.Write(o);
}

static IEnumerable<RunConfig> BuildSweep(RunConfig basis, string sweep, int[] frontiers, int[] caps)
{
    bool sweepFrontier = sweep is "frontier" or "both" or "all";
    bool sweepCap = sweep is "interval" or "both" or "all";
    var fs = sweepFrontier ? frontiers : new[] { basis.Frontier };
    var cs = sweepCap ? caps : new[] { basis.IntervalCap };
    foreach (var f in fs)
    foreach (var c in cs)
        yield return basis with { Frontier = f, IntervalCap = c };
}

static IEnumerable<string> BuildAllBots() => new[] { "random", "always-right", "always-wrong", "chance-0.25", "chance-0.50", "chance-0.75", "learner", "streak-3", "streak-5" };

static bool IsKana(Word w) =>
    w.Id.StartsWith("h-", StringComparison.Ordinal) ||
    w.Id.StartsWith("k-", StringComparison.Ordinal) ||
    w.Tags.Contains("hiragana") ||
    w.Tags.Contains("katakana");

static async Task<string> RunOne(RunConfig cfg)
{
    var rng = new Random(cfg.Seed);
    var vocab = new MemoryVocabularyService(cfg.VocabPath, cfg.Limit);
    var store = new MemoryProficiencyStore(cfg.IntervalCap);
    var settings = new MemorySettingsService { SelectedLearningStrategy = cfg.Strategy };
    var gen = new QuestionGenerator(vocab, store, settings)
    {
        // Per-instance tunables — safe to set distinct values across parallel runs.
        NewTermFrontierSize = cfg.Frontier
    };

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
        if (!correct)
            await store.RecordConfusionAsync(q.Target.Id, q.Options[pickIdx].Word.Id);
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
    var tunables = $"frontier={cfg.Frontier} cap={cfg.IntervalCap}";
    var rendered = analyzer.Render(store, eligible, bot.Name, cfg.Strategy, tunables);
    if (cfg.CsvOut is { } path)
    {
        var fileName = Path.GetFileNameWithoutExtension(path) + "-" + bot.Name + "-" + cfg.Strategy + Path.GetExtension(path);
        var full = Path.Combine(Path.GetDirectoryName(path) ?? ".", fileName);
        analyzer.DumpCsv(full);
        rendered += $"  csv               : {full}\n";
    }
    return rendered;
}

static IAnswerBot ResolveBot(string name, Func<string, double> proficiencyOf)
{
    if (name.StartsWith("chance-", StringComparison.OrdinalIgnoreCase))
    {
        var p = double.Parse(name["chance-".Length..], System.Globalization.CultureInfo.InvariantCulture);
        return new ChanceBot(p);
    }
    if (name.StartsWith("streak-", StringComparison.OrdinalIgnoreCase))
    {
        var k = int.Parse(name["streak-".Length..]);
        return new StreakBot(k);
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
            case "--frontier":  cfg = cfg with { Frontier = int.Parse(Next()) }; break;
            case "--interval-cap": cfg = cfg with { IntervalCap = int.Parse(Next()) }; break;
            case "--sweep":     cfg = cfg with { Sweep = Next().ToLowerInvariant() }; break;
            case "--help":
            case "-h":
                Console.WriteLine("StrategySim — exercises QuestionGenerator with synthetic answer bots.");
                Console.WriteLine();
                Console.WriteLine("Options:");
                Console.WriteLine("  --vocab PATH      Path to vocabulary.json (default: project Resources/Raw)");
                Console.WriteLine("  --bot NAME        random|always-right|always-wrong|learner|chance-0.50|streak-5  (default: learner)");
                Console.WriteLine("  --strategy NAME   Neutral|Spaced|QuickReview|WeakFocus|Fsrs  (default: Fsrs)");
                Console.WriteLine("  --turns N         Question count (default: 17500 — ~6mo at 100/day)");
                Console.WriteLine("  --limit N         Cap pool to first N words (default: full)");
                Console.WriteLine("  --seed N          RNG seed for repeatability (default: 1234)");
                Console.WriteLine("  --csv PATH        Write per-turn dump to PATH (filename gets bot+strategy suffix)");
                Console.WriteLine("  --all             Run every bot × every strategy and print all summaries");
                Console.WriteLine("  --frontier N      Override NewTermFrontierSize (default: 12)");
                Console.WriteLine("  --interval-cap N  Override the spaced-interval cap (default: 250)");
                Console.WriteLine("  --sweep KIND      frontier | interval | both — sweep tunables for the chosen bot/strategy");
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
    public required int Frontier { get; init; }
    public required int IntervalCap { get; init; }
    public required string? Sweep { get; init; }
}
