using LearnJP.ViewModels;

namespace LearnJP.Views;

public partial class ProgressPage : ContentPage
{
    private readonly ProgressViewModel _vm;
    private readonly ProgressChartDrawable _chartDrawable = new();

    public ProgressPage(ProgressViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = _vm;

        ProgressChart.Drawable = _chartDrawable;
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ProgressViewModel.ChartValues))
            {
                _chartDrawable.Values = _vm.ChartValues;
                ProgressChart.Invalidate();
            }
        };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.RefreshAsync();
    }
}
