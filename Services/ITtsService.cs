namespace LearnJP.Services;

/// <summary>
/// Language-neutral TTS surface. The locale and voice handed to the underlying provider are
/// resolved from the active <see cref="LearnJP.Models.LanguagePack"/>, so callers only ever
/// supply text — they don't need to know which language is active.
/// </summary>
public interface ITtsService
{
    /// <summary>Speak <paramref name="text"/> using the active language pack's locale and the
    /// current provider's voice for that pack. No-op when TTS is disabled, no language pack is
    /// active, or the active pack has no voice configured for the active provider.</summary>
    Task SpeakAsync(string text, CancellationToken ct = default);

    /// <summary>
    /// Stops any currently-playing or in-flight system-TTS utterance owned by this service.
    /// Does not affect already-rendered Azure clips that are mid-playback through the audio
    /// pipeline (those finish on their own), and does not prevent a subsequent
    /// <see cref="SpeakAsync"/> call from starting fresh.
    /// </summary>
    void CancelSpeaking();

    /// <summary>
    /// Warms the persistent cache for upcoming clips in the active language so that when one
    /// of them is requested, the audio is already on disk. Cheap no-op for providers without
    /// a persistent cache (e.g. system TTS).
    /// </summary>
    Task PrefetchAsync(IEnumerable<string> texts, CancellationToken ct = default);
}
