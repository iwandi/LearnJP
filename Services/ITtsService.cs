namespace LearnJP.Services;

/// <summary>
/// Language-neutral TTS surface. The locale and voice handed to the underlying provider are
/// resolved from the active <see cref="LearnJP.Models.LanguagePack"/>, so callers only ever
/// supply text — they don't need to know which language is active.
/// </summary>
public interface ITtsService
{
    /// <summary>Speak <paramref name="text"/> using the active language pack's locale and the
    /// current provider's voice for that pack. <paramref name="wordId"/> is the vocabulary ID
    /// of the word being spoken; when provided, the service will attempt to serve pre-generated
    /// bundled audio before falling back to the runtime cache or live synthesis. No-op when TTS
    /// is disabled, no language pack is active, or the active pack has no voice configured for
    /// the active provider.</summary>
    Task SpeakAsync(string text, string? wordId = null, CancellationToken ct = default);

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
    /// Each item is a <c>(wordId, text)</c> pair — the word ID enables bundled-asset lookup,
    /// and the text is the form handed to the TTS engine.
    /// </summary>
    Task PrefetchAsync(IEnumerable<(string wordId, string text)> items, CancellationToken ct = default);
}
