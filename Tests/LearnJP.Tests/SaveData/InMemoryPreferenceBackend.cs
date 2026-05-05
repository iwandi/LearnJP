using LearnJP.Services;

namespace LearnJP.Tests.SaveData;

/// <summary>
/// Test double for <see cref="IPreferenceBackend"/>. Mirrors the on-disk types of
/// <c>Microsoft.Maui.Storage.Preferences</c> so the same value coercion bugs surface here
/// as they would in production.
/// </summary>
internal sealed class InMemoryPreferenceBackend : IPreferenceBackend
{
    private readonly Dictionary<string, object> _store = new();

    public IReadOnlyDictionary<string, object> Snapshot() => _store;

    public T Get<T>(string key, T defaultValue) =>
        _store.TryGetValue(key, out var v) && v is T t ? t : defaultValue;

    public void Set<T>(string key, T value) => _store[key] = value!;
}
