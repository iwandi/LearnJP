namespace LearnJP.Services;

public enum SoundEffect { Click, Correct, Wrong }

public interface ISoundService
{
    /// <summary>Plays the effect asynchronously and returns its expected duration.</summary>
    TimeSpan Play(SoundEffect effect);
    void PlayWav(byte[] wavBytes);
}
