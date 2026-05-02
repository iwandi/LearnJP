namespace LearnJP.Services;

public enum SoundEffect { Click, Correct, Wrong }

public interface ISoundService
{
    /// <summary>Plays the effect asynchronously and returns its expected duration.</summary>
    TimeSpan Play(SoundEffect effect);
    /// <summary>
    /// Plays a 16-bit PCM WAV from memory. <paramref name="volume"/> is a 0..1 multiplier
    /// applied to the samples at playback time so the supplied buffer (often cached) is not mutated.
    /// </summary>
    void PlayWav(byte[] wavBytes, double volume = 1.0);
}
