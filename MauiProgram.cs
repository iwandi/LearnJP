using LearnJP.Services;
using LearnJP.ViewModels;
using LearnJP.Views;
using Microsoft.Extensions.Logging;

namespace LearnJP;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();

        builder.Services.AddSingleton<IVocabularyService, VocabularyService>();
        builder.Services.AddSingleton<IProficiencyStore, ProficiencyStore>();
        builder.Services.AddSingleton<IQuestionGenerator, QuestionGenerator>();
        builder.Services.AddSingleton<ITtsService, TtsService>();
        builder.Services.AddSingleton<ISoundService, SoundService>();
        builder.Services.AddSingleton<ISettingsService, SettingsService>();

        builder.Services.AddSingleton<QuizViewModel>();
        builder.Services.AddSingleton<ProgressViewModel>();
        builder.Services.AddSingleton<SettingsViewModel>();

        builder.Services.AddSingleton<QuizPage>();
        builder.Services.AddSingleton<ProgressPage>();
        builder.Services.AddSingleton<SettingsPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
