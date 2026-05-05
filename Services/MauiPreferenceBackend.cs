namespace LearnJP.Services;

/// <summary>
/// Production <see cref="IPreferenceBackend"/> wrapping <see cref="Preferences.Default"/>.
/// Falls back to an in-memory dictionary if the platform's preferences API throws (e.g.
/// when the host process isn't a fully initialised MAUI app — happens in some tooling
/// paths). Lives in its own file so unit tests can link <see cref="SettingsService"/>
/// without needing the MAUI Essentials reference.
/// </summary>
public sealed class MauiPreferenceBackend : IPreferenceBackend
{
    private readonly Dictionary<string, object> _fallback = new();
    private bool _preferencesAvailable = true;

    public T Get<T>(string key, T defaultValue)
    {
        if (_preferencesAvailable)
        {
            try { return Preferences.Default.Get(key, defaultValue); }
            catch { _preferencesAvailable = false; }
        }
        return _fallback.TryGetValue(key, out var v) && v is T t ? t : defaultValue;
    }

    public void Set<T>(string key, T value)
    {
        if (_preferencesAvailable)
        {
            try { Preferences.Default.Set(key, value); return; }
            catch { _preferencesAvailable = false; }
        }
        _fallback[key] = value!;
    }
}
