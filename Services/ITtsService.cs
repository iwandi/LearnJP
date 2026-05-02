namespace LearnJP.Services;

public interface ITtsService
{
    Task SpeakJapaneseAsync(string text, CancellationToken ct = default);
    Task SpeakEnglishAsync(string text, CancellationToken ct = default);
    void Cancel();

    /// <summary>
    /// Warms the cache for upcoming Japanese clips without playing them. Cheap no-op for
    /// providers that don't have a persistent cache (e.g. system TTS).
    /// </summary>
    Task PrefetchJapaneseAsync(IEnumerable<string> texts, CancellationToken ct = default);
}
