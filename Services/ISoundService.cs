namespace LearnJP.Services;

public enum SoundEffect { Click, Correct, Wrong }

public interface ISoundService
{
    void Play(SoundEffect effect);
    void PlayWav(byte[] wavBytes);
}
