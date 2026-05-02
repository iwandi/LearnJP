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
}
