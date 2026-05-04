using LearnJP.Services;

namespace LearnJP;

public partial class AppShell : Shell
{
    private readonly ILocalizationService _loc;

    public AppShell(ILocalizationService loc)
    {
        _loc = loc;
        InitializeComponent();
        ApplyTabTitles();
        _loc.PropertyChanged += (_, _) => ApplyTabTitles();
    }

    private void ApplyTabTitles()
    {
        TabLanguage.Title = _loc["tab_language"];
        TabQuiz.Title     = _loc["tab_quiz"];
        TabMode.Title     = _loc["tab_mode"];
        TabProgress.Title = _loc["tab_progress"];
        TabSettings.Title = _loc["tab_settings"];
    }
}
