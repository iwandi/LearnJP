using LearnJP.Models;

namespace LearnJP.Services;

public interface ILanguagePackService
{
    Task EnsureLoadedAsync();

    /// <summary>All language packs discovered via the bundled index.</summary>
    IReadOnlyList<LanguagePack> All { get; }

    /// <summary>The currently selected pack. Falls back to the first available pack.</summary>
    LanguagePack? Active { get; }

    /// <summary>Persists the new active language id and refreshes <see cref="Active"/>.</summary>
    Task SetActiveAsync(string id);

    /// <summary>Fired when <see cref="Active"/> changes — useful for downstream services that cache pack-derived state.</summary>
    event EventHandler? ActiveChanged;
}
