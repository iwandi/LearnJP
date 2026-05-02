using LearnJP.Models;

namespace LearnJP.Services;

public sealed class SettingsService : ISettingsService
{
    private const string KeyRomaji        = "settings.romaji_only";
    private const string KeyTts           = "settings.tts_enabled";
    private const string KeyFurigana      = "settings.force_furigana";
    private const string KeyRate          = "settings.tts_rate";
    private const string KeyTtsProvider   = "settings.tts_provider";
    private const string KeyAzureKey      = "settings.azure_key";
    private const string KeyAzureRegion   = "settings.azure_region";
    private const string KeyAzureJaVoice  = "settings.azure_ja_voice";
    private const string KeyAzureEnVoice  = "settings.azure_en_voice";
    private const string KeySystemVolume  = "settings.system_volume";
    private const string KeyAzureVolume   = "settings.azure_volume";

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

    public TtsProvider TtsProvider
    {
        get => (TtsProvider)Read(KeyTtsProvider, (int)TtsProvider.System);
        set => Write(KeyTtsProvider, (int)value);
    }

    public string AzureSpeechKey
    {
        get => Read(KeyAzureKey, string.Empty);
        set => Write(KeyAzureKey, value ?? string.Empty);
    }

    public string AzureSpeechRegion
    {
        get => Read(KeyAzureRegion, "westeurope");
        set => Write(KeyAzureRegion, value ?? string.Empty);
    }

    public string AzureJapaneseVoice
    {
        get => Read(KeyAzureJaVoice, "ja-JP-NanamiNeural");
        set => Write(KeyAzureJaVoice, value ?? string.Empty);
    }

    public string AzureEnglishVoice
    {
        get => Read(KeyAzureEnVoice, "en-US-JennyNeural");
        set => Write(KeyAzureEnVoice, value ?? string.Empty);
    }

    public double SystemTtsVolume
    {
        get => Math.Clamp(Read(KeySystemVolume, 1.0), 0.0, 1.0);
        set => Write(KeySystemVolume, Math.Clamp(value, 0.0, 1.0));
    }

    public double AzureTtsVolume
    {
        get => Math.Clamp(Read(KeyAzureVolume, 1.0), 0.0, 1.0);
        set => Write(KeyAzureVolume, Math.Clamp(value, 0.0, 1.0));
    }
}
