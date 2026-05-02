namespace LearnJP.Models;

public enum TtsProvider
{
    System,
    Azure
}

public static class TtsProviderExtensions
{
    public static string Display(this TtsProvider p) => p switch
    {
        TtsProvider.System => "System (built-in)",
        TtsProvider.Azure  => "Azure Cognitive Services",
        _ => p.ToString()
    };
}
