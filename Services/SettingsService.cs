namespace LearnJP.Services;

public sealed class SettingsService : ISettingsService
{
    private const string KeyRomaji = "settings.romaji_only";
    private const string KeyTts = "settings.tts_enabled";
    private const string KeyFurigana = "settings.force_furigana";
    private const string KeyRate = "settings.tts_rate";

    private readonly Dictionary<string, object> _fallback = new();
    private bool _preferencesAvailable = true;

    private T Read<T>(string key, T defaultValue)
    {
        if (_preferencesAvailable)
        {
            try { return Preferences.Default.Get(key, defaultValue); }
            catch { _preferencesAvailable = false; }
        }
        return _fallback.TryGetValue(key, out var v) && v is T t ? t : defaultValue;
    }

    private void Write<T>(string key, T value)
    {
        if (_preferencesAvailable)
        {
            try { Preferences.Default.Set(key, value); return; }
            catch { _preferencesAvailable = false; }
        }
        _fallback[key] = value!;
    }

    public bool RomajiOnly      { get => Read(KeyRomaji,   false); set => Write(KeyRomaji,   value); }
    public bool TtsEnabled      { get => Read(KeyTts,      true);  set => Write(KeyTts,      value); }
    public bool ForceFurigana   { get => Read(KeyFurigana, false); set => Write(KeyFurigana, value); }
    public double TtsRate       { get => Read(KeyRate,     0.9);   set => Write(KeyRate,     value); }
}
