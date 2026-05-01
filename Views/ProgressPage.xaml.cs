using LearnJP.ViewModels;

namespace LearnJP.Views;

public partial class ProgressPage : ContentPage
{
    private readonly ProgressViewModel _vm;
    public ProgressPage(ProgressViewModel vm)
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
}
