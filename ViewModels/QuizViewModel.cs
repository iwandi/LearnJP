using System.Collections.ObjectModel;
using LearnJP.Models;
using LearnJP.Services;

namespace LearnJP.ViewModels;

public sealed class QuizOptionVm : BaseViewModel
{
    private string _state = "idle"; // idle | correct | wrong | revealed

    public required QuestionOption Source { get; init; }
    public required bool IsJapaneseSide { get; init; }

    public string Text => Source.DisplayText;
    public string State { get => _state; set => SetProperty(ref _state, value); }
    public bool ShowSpeakButton => IsJapaneseSide;

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
    private static readonly TimeSpan FeedbackCorrect = TimeSpan.FromMilliseconds(700);
    private static readonly TimeSpan FeedbackWrong   = TimeSpan.FromMilliseconds(1600);
    // Small silent gap between an effect ending and TTS starting so they don't blur.
    private static readonly TimeSpan PostEffectPause = TimeSpan.FromMilliseconds(120);

    private readonly IQuestionGenerator _gen;
    private readonly IProficiencyStore _store;
    private readonly ITtsService _tts;
    private readonly ISettingsService _settings;
    private readonly ISoundService _sounds;

    private Question? _current;
    private string _prompt = "";
    private string? _promptFurigana;
    private string _directionLabel = "";
    private bool _isAnswered;
    private bool _isLoading;
    private bool _showPromptSpeakButton;
    private bool _countForProficiency = true;
    private LearningStrategy _selectedStrategy = LearningStrategy.Neutral;
    private CancellationTokenSource? _autoAdvanceCts;
    private Task _ttsTask = Task.CompletedTask;

    public ObservableCollection<QuizOptionVm> Options { get; } = new();
    public ObservableCollection<LearningStrategy> Strategies { get; } =
        new(Enum.GetValues<LearningStrategy>());

    public string Prompt { get => _prompt; private set => SetProperty(ref _prompt, value); }
    public string? PromptFurigana { get => _promptFurigana; private set { SetProperty(ref _promptFurigana, value); OnPropertyChanged(nameof(HasFurigana)); } }
    public bool HasFurigana => !string.IsNullOrEmpty(_promptFurigana);
    public string DirectionLabel { get => _directionLabel; private set => SetProperty(ref _directionLabel, value); }
    public bool IsAnswered { get => _isAnswered; private set => SetProperty(ref _isAnswered, value); }
    public bool IsLoading { get => _isLoading; private set => SetProperty(ref _isLoading, value); }
    public bool ShowPromptSpeakButton { get => _showPromptSpeakButton; private set => SetProperty(ref _showPromptSpeakButton, value); }
    public bool CountForProficiency { get => _countForProficiency; set => SetProperty(ref _countForProficiency, value); }
    public LearningStrategy SelectedStrategy { get => _selectedStrategy; set => SetProperty(ref _selectedStrategy, value); }

    private string _activeFilterDisplay = "";
    public string ActiveFilterDisplay { get => _activeFilterDisplay; private set { SetProperty(ref _activeFilterDisplay, value); OnPropertyChanged(nameof(HasActiveFilter)); } }
    public bool HasActiveFilter => !string.IsNullOrEmpty(_activeFilterDisplay);

    private string _lastSeenFilter = string.Empty;

    /// <summary>
    /// Called from the page's OnAppearing — picks up tag-filter changes made on the Filter tab
    /// and forces a fresh question if the filter changed.
    /// </summary>
    public async Task SyncActiveFilterAsync()
    {
        var current = (_settings.ActiveTagFilter ?? string.Empty).Trim();
        ActiveFilterDisplay = string.IsNullOrEmpty(current) ? string.Empty : $"Filter: {current}";

        if (!string.Equals(current, _lastSeenFilter, StringComparison.OrdinalIgnoreCase))
        {
            _lastSeenFilter = current;
            // Don't trigger a fresh load on the very first appearance — OnAppearing already does that.
            if (_current is not null) await LoadNextAsync();
        }
    }

    public QuizViewModel(IQuestionGenerator gen, IProficiencyStore store, ITtsService tts, ISettingsService settings, ISoundService sounds)
    {
        _gen = gen;
        _store = store;
        _tts = tts;
        _settings = settings;
        _sounds = sounds;
    }

    public async Task LoadNextAsync()
    {
        CancelAutoAdvance();
        IsLoading = true;
        try
        {
            var q = await _gen.NextAsync(_selectedStrategy);
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
            ShowPromptSpeakButton = q.Direction == QuestionDirection.JapaneseToEnglish;

            var optionsAreJapanese = q.Direction == QuestionDirection.EnglishToJapanese;
            Options.Clear();
            foreach (var o in q.Options)
                Options.Add(new QuizOptionVm { Source = o, IsJapaneseSide = optionsAreJapanese });

            IsAnswered = false;

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
        var effectDuration = _sounds.Play(correct ? SoundEffect.Correct : SoundEffect.Wrong);
        vm.State = correct ? "correct" : "wrong";
        vm.RaiseColors();

        if (!correct)
        {
            foreach (var o in Options)
                if (o.Source.IsCorrect) { o.State = "revealed"; o.RaiseColors(); }
        }

        if (_countForProficiency)
        {
            try { await _store.RecordAsync(_current.Target.Id, _current.Criterion, correct); }
            catch { /* best effort */ }
        }

        // Wait for the effect to finish (plus a small gap), then speak the JP — covers EN→JP too.
        var ttsText = _current.TtsText;
        _ttsTask = SpeakAfterAsync(effectDuration + PostEffectPause, ttsText);

        await ScheduleAutoAdvance(correct ? FeedbackCorrect : FeedbackWrong);
    }

    private async Task SpeakAfterAsync(TimeSpan delay, string text)
    {
        try
        {
            if (delay > TimeSpan.Zero) await Task.Delay(delay);
            await _tts.SpeakJapaneseAsync(text);
        }
        catch { /* ignore */ }
    }

    public async Task SkipAsync()
    {
        _sounds.Play(SoundEffect.Click);
        CancelAutoAdvance();
        await LoadNextAsync();
    }

    public async Task DontKnowAsync()
    {
        if (IsAnswered || _current is null) return;
        IsAnswered = true;
        var effectDuration = _sounds.Play(SoundEffect.Wrong);

        foreach (var o in Options)
            if (o.Source.IsCorrect) { o.State = "revealed"; o.RaiseColors(); }

        if (_countForProficiency)
        {
            try { await _store.RecordAsync(_current.Target.Id, _current.Criterion, false); }
            catch { /* best effort */ }
        }

        _ttsTask = SpeakAfterAsync(effectDuration + PostEffectPause, _current.TtsText);

        await ScheduleAutoAdvance(FeedbackWrong);
    }

    public async Task SpeakCurrentAsync()
    {
        if (_current is null) return;
        var effectDuration = _sounds.Play(SoundEffect.Click);
        await SpeakAfterAsync(effectDuration + PostEffectPause, _current.TtsText);
    }

    public async Task SpeakOptionAsync(QuizOptionVm opt)
    {
        var kana = opt.Source.Word.Kana;
        if (string.IsNullOrWhiteSpace(kana)) return;
        var effectDuration = _sounds.Play(SoundEffect.Click);
        await SpeakAfterAsync(effectDuration + PostEffectPause, kana);
    }

    private async Task ScheduleAutoAdvance(TimeSpan delay)
    {
        CancelAutoAdvance();
        _autoAdvanceCts = new CancellationTokenSource();
        var token = _autoAdvanceCts.Token;
        try
        {
            // Wait for both the visual feedback delay and the queued TTS to finish.
            var feedback = Task.Delay(delay, token);
            try { await Task.WhenAll(feedback, _ttsTask); }
            catch (OperationCanceledException) { return; }

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
}
