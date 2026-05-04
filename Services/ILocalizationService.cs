using System.ComponentModel;

namespace LearnJP.Services;

/// <summary>
/// Provides localized UI strings. Auto-detects the system display language
/// (<see cref="System.Globalization.CultureInfo.CurrentUICulture"/>) and falls back to
/// English for any unsupported language. The active language can be overridden via
/// <see cref="ISettingsService.UiLanguageOverride"/>.
/// </summary>
public interface ILocalizationService : INotifyPropertyChanged
{
    /// <summary>Returns the localized string for <paramref name="key"/>, falling back to
    /// English and then to the key itself if the key is not found.</summary>
    string this[string key] { get; }

    /// <summary>The ISO 639-1 code of the currently active language (e.g. "en", "de").</summary>
    string CurrentLanguage { get; }

    /// <summary>All language codes supported by this service (includes "en" and "de").</summary>
    IReadOnlyList<string> SupportedLanguages { get; }

    /// <summary>Applies a language override and persists it. Pass <c>null</c> or empty
    /// to revert to auto-detection from the system display language.</summary>
    void ApplyOverride(string? langCode);
}
