namespace LearnJP.Services;

/// <summary>
/// Minimal key-value backend behind <see cref="SettingsService"/>. Exists so the settings
/// layer can be unit-tested without a MAUI runtime: production wires up
/// <see cref="MauiPreferenceBackend"/>, tests pass an in-memory implementation.
/// </summary>
public interface IPreferenceBackend
{
    T Get<T>(string key, T defaultValue);
    void Set<T>(string key, T value);
}
