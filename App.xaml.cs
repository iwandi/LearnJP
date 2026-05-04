using LearnJP.Services;

namespace LearnJP;

public partial class App : Application
{
    private readonly ILocalizationService _loc;

    public App(ILocalizationService loc)
    {
        _loc = loc;
        InitializeComponent();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(new AppShell(_loc));

#if WINDOWS
        const double width = 412;   // ≈ Pixel 5 logical width
        const double height = 892;  // ≈ Pixel 5 logical height
        window.Width = width;
        window.Height = height;
        window.MinimumWidth = 360;
        window.MinimumHeight = 640;
#endif

        return window;
    }
}
