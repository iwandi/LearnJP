using LearnJP.ViewModels;

namespace LearnJP.Views;

public partial class LanguageSelectionPage : ContentPage
{
    private readonly LanguageSelectionViewModel _vm;

    public LanguageSelectionPage(LanguageSelectionViewModel vm)
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

    private async void OnSelectClicked(object? sender, EventArgs e)
    {
        if (sender is Button { BindingContext: LanguageOption opt })
            await _vm.SelectAsync(opt);
    }
}
