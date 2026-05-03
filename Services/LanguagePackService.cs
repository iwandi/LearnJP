using System.Diagnostics;
using System.Text.Json;
using LearnJP.Models;

namespace LearnJP.Services;

public sealed class LanguagePackService : ILanguagePackService
{
    private readonly ISettingsService _settings;
    private readonly List<LanguagePack> _packs = new();
    private LanguagePack? _active;
    private bool _loaded;

    public LanguagePackService(ISettingsService settings) { _settings = settings; }

    public IReadOnlyList<LanguagePack> All => _packs;
    public LanguagePack? Active => _active;
    public event EventHandler? ActiveChanged;

    public async Task EnsureLoadedAsync()
    {
        if (_loaded) return;

        var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        // Read the manifest first; each entry is a relative file under Resources/Raw/Languages/.
        try
        {
            await using var idxStream = await FileSystem.OpenAppPackageFileAsync("Languages/index.json");
            var idx = await JsonSerializer.DeserializeAsync<LanguagePackIndex>(idxStream, opts);
            if (idx is not null)
            {
                foreach (var file in idx.Packs)
                {
                    try
                    {
                        await using var packStream = await FileSystem.OpenAppPackageFileAsync($"Languages/{file}");
                        var pack = await JsonSerializer.DeserializeAsync<LanguagePack>(packStream, opts);
                        if (pack is not null && !string.IsNullOrWhiteSpace(pack.Id))
                            _packs.Add(pack);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[LanguagePackService] failed to load {file}: {ex.Message}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LanguagePackService] index load failed: {ex.Message}");
        }

        // Resolve active id from settings; fall back to the first pack we found.
        var activeId = _settings.ActiveLanguageId;
        _active = _packs.FirstOrDefault(p => string.Equals(p.Id, activeId, StringComparison.OrdinalIgnoreCase))
                  ?? _packs.FirstOrDefault();
        _loaded = true;
    }

    public Task SetActiveAsync(string id)
    {
        var match = _packs.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
        if (match is null) return Task.CompletedTask;
        if (_active?.Id == match.Id) return Task.CompletedTask;
        _active = match;
        _settings.ActiveLanguageId = match.Id;
        ActiveChanged?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }
}
