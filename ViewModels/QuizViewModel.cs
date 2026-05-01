using System.Collections.ObjectModel;
using LearnJP.Models;
using LearnJP.Services;

namespace LearnJP.ViewModels;

public sealed class QuizOptionVm : BaseViewModel
{
    private string _state = "idle"; // idle | correct | wrong | revealed
    public required QuestionOption Source { get; init; }
    public string Text => Source.DisplayText;
    public string State { get => _state; set => SetProperty(ref _state, value); }

    public Color BackgroundColor => State switch
    {
        "correct"  => Color.FromArgb("#4CAF7A"),
        "wrong"    => Color.FromArgb("#E5556B"),
        "revealed" => Color.FromArgb("#4CAF7A"),
        _ => Application.Current?.RequestedTheme == AppTheme.Dark
            ? Color.FromArgb("#23233A")
            : Color.FromArgb("#F2F3FB")
    };

    public Color TextColor => State == "idle"
        ? (Application.Current?.RequestedTheme == AppTheme.Dark ? Colors.White : Color.FromArgb("#1A1A2E"))
        : Colors.White;

    public void RaiseColors()
    {
        OnPropertyChanged(nameof(BackgroundColor));
        OnPropertyChanged(nameof(TextColor));
    }
}

public sealed class QuizViewModel : BaseViewModel
{
    // Time the colored feedback stays on screen before auto-advancing.
    private static readonly TimeSpan FeedbackCorrect = TimeSpan.FromMilliseconds(700);
    private static readonly TimeSpan FeedbackWrong   = TimeSpan.FromMilliseconds(1600);

    private readonly IQuestionGenerator _gen;
    private readonly IProficiencyStore _store;
    private readonly ITtsService _tts;
    private readonly ISettingsService _settings;

    private Question? _current;
    private string _prompt = "";
    private string? _promptFurigana;
    private string _directionLabel = "";
    private string _criterionLabel = "";
    private string _hintLabel = "";
    private string _streakLabel = "";
    private bool _isAnswered;
    private bool _isLoading;
    private int _streak;
    private int _sessionAnswered;
    private int _sessionCorrect;
    private CancellationTokenSource? _autoAdvanceCts;

    public ObservableCollection<QuizOptionVm> Options { get; } = new();

    public string Prompt { get => _prompt; private set => SetProperty(ref _prompt, value); }
    public string? PromptFurigana { get => _promptFurigana; private set { SetProperty(ref _promptFurigana, value); OnPropertyChanged(nameof(HasFurigana)); } }
    public bool HasFurigana => !string.IsNullOrEmpty(_promptFurigana);
    public string DirectionLabel { get => _directionLabel; private set => SetProperty(ref _directionLabel, value); }
    public string CriterionLabel { get => _criterionLabel; private set => SetProperty(ref _criterionLabel, value); }
    public string HintLabel { get => _hintLabel; private set => SetProperty(ref _hintLabel, value); }
    public string StreakLabel { get => _streakLabel; private set => SetProperty(ref _streakLabel, value); }
    public bool IsAnswered { get => _isAnswered; private set => SetProperty(ref _isAnswered, value); }
    public bool IsLoading { get => _isLoading; private set => SetProperty(ref _isLoading, value); }
    public int SessionAnswered { get => _sessionAnswered; private set => SetProperty(ref _sessionAnswered, value); }
    public int SessionCorrect { get => _sessionCorrect; private set => SetProperty(ref _sessionCorrect, value); }

    public QuizViewModel(IQuestionGenerator gen, IProficiencyStore store, ITtsService tts, ISettingsService settings)
    {
        _gen = gen;
        _store = store;
        _tts = tts;
        _settings = settings;
    }

    public async Task LoadNextAsync()
    {
        CancelAutoAdvance();
        IsLoading = true;
        try
        {
            var q = await _gen.NextAsync();
            if (q is null)
            {
                Prompt = "No vocabulary loaded.";
                Options.Clear();
                return;
            }
            _current = q;
            Prompt = q.Prompt;
            PromptFurigana = q.PromptFurigana;
            DirectionLabel = q.Direction == QuestionDirection.JapaneseToEnglish ? "Japanese → English" : "English → Japanese";
            CriterionLabel = q.Criterion.Display();
            HintLabel = $"Proficiency {q.TargetProficiencyAtAsk:0}%";

            Options.Clear();
            foreach (var o in q.Options)
                Options.Add(new QuizOptionVm { Source = o });

            IsAnswered = false;
            UpdateStreakLabel();

            if (q.Direction == QuestionDirection.JapaneseToEnglish)
                _ = _tts.SpeakJapaneseAsync(q.TtsText);
        }
        finally { IsLoading = false; }
    }

    public async Task SelectOptionAsync(QuizOptionVm vm)
    {
        if (IsAnswered || _current is null) return;
        IsAnswered = true;

        var correct = vm.Source.IsCorrect;
        vm.State = correct ? "correct" : "wrong";
        vm.RaiseColors();

        if (!correct)
        {
            foreach (var o in Options)
                if (o.Source.IsCorrect) { o.State = "revealed"; o.RaiseColors(); }
        }

        try { await _store.RecordAsync(_current.Target.Id, _current.Criterion, correct); }
        catch { /* best effort */ }

        SessionAnswered++;
        if (correct) { SessionCorrect++; _streak++; } else { _streak = 0; }
        UpdateStreakLabel();

        // Always read the Japanese after answering.
        _ = _tts.SpeakJapaneseAsync(_current.TtsText);

        await ScheduleAutoAdvance(correct ? FeedbackCorrect : FeedbackWrong);
    }

    public async Task SkipAsync()
    {
        CancelAutoAdvance();
        await LoadNextAsync();
    }

    public async Task SpeakCurrentAsync()
    {
        if (_current is null) return;
        try { await _tts.SpeakJapaneseAsync(_current.TtsText); } catch { /* ignore */ }
    }

    private async Task ScheduleAutoAdvance(TimeSpan delay)
    {
        CancelAutoAdvance();
        _autoAdvanceCts = new CancellationTokenSource();
        var token = _autoAdvanceCts.Token;
        try
        {
            await Task.Delay(delay, token);
            if (!token.IsCancellationRequested)
                await LoadNextAsync();
        }
        catch (OperationCanceledException) { /* user advanced manually */ }
    }

    private void CancelAutoAdvance()
    {
        try { _autoAdvanceCts?.Cancel(); } catch { /* ignore */ }
        _autoAdvanceCts = null;
    }

    private void UpdateStreakLabel()
    {
        var pct = SessionAnswered == 0 ? 0 : (int)Math.Round(100.0 * SessionCorrect / SessionAnswered);
        StreakLabel = $"Session: {SessionCorrect}/{SessionAnswered} ({pct}%) · Streak {_streak}";
    }
}
