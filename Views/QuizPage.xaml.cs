using LearnJP.Services;
using LearnJP.ViewModels;

namespace LearnJP.Views;

public partial class QuizPage : ContentPage
{
    private readonly QuizViewModel _vm;
    private readonly ISoundService _sounds;
    private bool _firstLoad = true;

    public QuizPage(QuizViewModel vm, ISoundService sounds)
    {
        InitializeComponent();
        _vm = vm;
        _sounds = sounds;
        BindingContext = _vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_firstLoad)
        {
            _firstLoad = false;
            await _sounds.PreloadAsync();
            await _vm.LoadNextAsync();
        }
        await _vm.SyncActiveFilterAsync();
    }

    private async void OnOptionClicked(object? sender, EventArgs e)
    {
        if (sender is Button { BindingContext: QuizOptionVm opt })
            await _vm.SelectOptionAsync(opt);
    }

    private async void OnOptionSpeakClicked(object? sender, EventArgs e)
    {
        if (sender is Button { BindingContext: QuizOptionVm opt })
            await _vm.SpeakOptionAsync(opt);
    }

    private async void OnSkipClicked(object? sender, EventArgs e) => await _vm.SkipAsync();

    private async void OnDontKnowClicked(object? sender, EventArgs e) => await _vm.DontKnowAsync();

    private async void OnSpeakClicked(object? sender, EventArgs e) => await _vm.SpeakCurrentAsync();

    private async void OnReinforceClicked(object? sender, EventArgs e) => await _vm.ToggleReinforcedAsync();
}
