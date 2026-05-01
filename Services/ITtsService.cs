namespace LearnJP.Services;

public interface ITtsService
{
    Task SpeakJapaneseAsync(string text, CancellationToken ct = default);
    Task SpeakEnglishAsync(string text, CancellationToken ct = default);
    void Cancel();
}
