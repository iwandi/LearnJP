using LearnJP.ViewModels;

namespace LearnJP.Views;

public partial class TagFilterPage : ContentPage
{
    private readonly TagFilterViewModel _vm;

    public TagFilterPage(TagFilterViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = _vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.RefreshAsync();
    }

    private void OnAutoModeClicked(object? sender, EventArgs e)  => _vm.SelectAutoProgression();
    private void OnManualClicked(object? sender, EventArgs e)    => _vm.SelectManual();

    private void OnIncludeClicked(object? sender, EventArgs e)
    {
        if (sender is Button { BindingContext: TagOption opt }) _vm.ToggleInclude(opt);
    }

    private void OnExcludeClicked(object? sender, EventArgs e)
    {
        if (sender is Button { BindingContext: TagOption opt }) _vm.ToggleExclude(opt);
    }
}
