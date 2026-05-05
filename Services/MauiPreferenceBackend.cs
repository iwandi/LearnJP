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
    /// <summary>
    /// Indirection over <see cref="Preferences.Default"/>. Production wires up a static-call
    /// implementation; tests can substitute one that throws to drive the fallback path.
    /// </summary>
    internal interface IPlatformPreferences
    {
        T Get<T>(string key, T defaultValue);
        void Set<T>(string key, T value);
    }

#if ANDROID || IOS || MACCATALYST || WINDOWS
    private sealed class StaticPreferences : IPlatformPreferences
    {
        public T Get<T>(string key, T defaultValue) => Preferences.Default.Get(key, defaultValue);
        public void Set<T>(string key, T value) => Preferences.Default.Set(key, value);
    }
#endif

    private readonly IPlatformPreferences _platform;
    private readonly Dictionary<string, object> _fallback = new();
    private bool _preferencesAvailable = true;

#if ANDROID || IOS || MACCATALYST || WINDOWS
    public MauiPreferenceBackend() : this(new StaticPreferences()) { }
#endif

    /// <summary>Test seam — substitute an alternative platform-preferences implementation.</summary>
    internal MauiPreferenceBackend(IPlatformPreferences platform)
    {
        _platform = platform ?? throw new ArgumentNullException(nameof(platform));
    }

    public T Get<T>(string key, T defaultValue)
    {
        if (_preferencesAvailable)
        {
            try { return _platform.Get(key, defaultValue); }
            catch { _preferencesAvailable = false; }
        }
        return _fallback.TryGetValue(key, out var v) && v is T t ? t : defaultValue;
    }

    public void Set<T>(string key, T value)
    {
        if (_preferencesAvailable)
        {
            try { _platform.Set(key, value); return; }
            catch { _preferencesAvailable = false; }
        }
        _fallback[key] = value!;
    }
}
