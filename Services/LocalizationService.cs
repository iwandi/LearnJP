using System.ComponentModel;
using System.Globalization;

namespace LearnJP.Services;

public sealed class LocalizationService : ILocalizationService
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private readonly ISettingsService _settings;
    private string _currentLanguage;

    private static readonly Dictionary<string, Dictionary<string, string>> Strings = BuildStrings();

    public LocalizationService(ISettingsService settings)
    {
        _settings = settings;
        _currentLanguage = ResolveLanguage(settings.UiLanguageOverride);
    }

    public string this[string key]
    {
        get
        {
            if (Strings.TryGetValue(_currentLanguage, out var dict) && dict.TryGetValue(key, out var value))
                return value;
            if (Strings.TryGetValue("en", out var fallback) && fallback.TryGetValue(key, out var fallbackValue))
                return fallbackValue;
            return key;
        }
    }

    public string CurrentLanguage => _currentLanguage;

    public IReadOnlyList<string> SupportedLanguages { get; } = new[] { "en", "de" };

    public void ApplyOverride(string? langCode)
    {
        _settings.UiLanguageOverride = langCode ?? string.Empty;
        var resolved = ResolveLanguage(langCode ?? string.Empty);
        if (resolved == _currentLanguage) return;
        _currentLanguage = resolved;
        // "Item[]" is the conventional property name for indexer change notification.
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
    }

    private static string ResolveLanguage(string? overrideCode)
    {
        if (!string.IsNullOrWhiteSpace(overrideCode))
        {
            // Accept full tags like "de-AT" — only the primary subtag matters.
            var code = overrideCode.ToLowerInvariant().Split('-')[0];
            if (Strings.ContainsKey(code)) return code;
        }

        // Auto-detect from the system display language (not the formatting locale).
        var ui = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.ToLowerInvariant();
        return Strings.ContainsKey(ui) ? ui : "en";
    }

    private static Dictionary<string, Dictionary<string, string>> BuildStrings() => new()
    {
        ["en"] = new(StringComparer.Ordinal)
        {
            // Tab bar
            ["tab_language"]                   = "Language",
            ["tab_quiz"]                       = "Quiz",
            ["tab_mode"]                       = "Customize",
            ["tab_customize"]                  = "Customize",
            ["tab_progress"]                   = "Progress",
            ["tab_settings"]                   = "Settings",

            // Language selection page
            ["lang_base_language"]             = "Base language",
            ["lang_active_target"]             = "Active target language",
            ["lang_select_button"]             = "Select",
            ["lang_active_button"]             = "Active",
            ["lang_learn_kana"]                = "Learn Kana",

            // Language names (shown in base-language picker)
            ["lang_english"]                   = "English",
            ["lang_german"]                    = "German",
            ["lang_french"]                    = "French",
            ["lang_spanish"]                   = "Spanish",
            ["lang_italian"]                   = "Italian",
            ["lang_portuguese"]                = "Portuguese",
            ["lang_dutch"]                     = "Dutch",
            ["lang_russian"]                   = "Russian",
            ["lang_chinese"]                   = "Chinese",
            ["lang_japanese"]                  = "Japanese",
            ["lang_korean"]                    = "Korean",

            // Quiz page
            ["quiz_hear_again"]                = "🔊  Hear it again",
            ["quiz_dont_know"]                 = "I don't know",
            ["quiz_skip"]                      = "Skip ▶",
            ["quiz_no_vocab"]                  = "No vocabulary loaded.",
            ["quiz_filter_prefix"]             = "Filter: ",

            // Progress page
            ["progress_total"]                 = "Total",
            ["progress_seen"]                  = "Seen",
            ["progress_known"]                 = "Known",
            ["progress_mastered"]              = "Mastered",
            ["progress_reset"]                 = "Reset progress",
            ["progress_search_placeholder"]    = "Search terms…",
            ["progress_reset_confirm_title"]   = "Reset Progress",
            ["progress_reset_confirm_message"] = "This will permanently delete all your progress. Are you sure?",
            ["progress_reset_confirm_yes"]     = "Reset",
            ["progress_reset_confirm_cancel"]  = "Cancel",
            // {0}=mastered, {1}=total, {2}=known, {3}=seen
            ["progress_summary_format"]        = "{0}/{1} mastered · {2} known · {3} encountered",

            // Tag filter / mode page
            ["filter_strategy"]                = "Strategy",
            ["filter_no_filter"]               = "(no filter — clear all)",
            ["filter_active_no_filter"]        = "No filter — drawing from the full vocabulary.",
            ["filter_active_prefix"]           = "Filter: ",
            ["filter_mode_auto"]               = "Auto Progression",
            ["filter_mode_no_filter"]          = "No Filter",
            ["filter_mode_manual"]             = "Manual",
            ["filter_active_auto_prefix"]      = "Auto: ",
            ["filter_progression_locked"]      = "Locked",
            ["filter_progression_unlocked"]    = "Unlocked",
            ["filter_progression_skipped"]     = "Skipped",

            // Customize tab
            ["customize_display_options"]      = "Language settings",

            // Settings page — sections
            ["settings_display"]               = "Display",
            ["settings_audio"]                 = "Audio",
            ["settings_behaviour"]             = "Behaviour",
            ["settings_about"]                 = "About",
            ["settings_language"]              = "Language",

            // Settings page — fields
            ["settings_speak_question"]        = "Autoplay vocal",
            ["settings_tts_provider"]          = "TTS provider",
            ["settings_system_volume"]         = "System TTS volume",
            ["settings_azure_volume"]          = "Azure TTS volume",
            ["settings_azure_key"]             = "Azure subscription key",
            ["settings_azure_key_placeholder"] = "paste your key here",
            ["settings_azure_region"]          = "Azure region",
            ["settings_azure_region_placeholder"] = "e.g. westeurope, eastus",
            ["settings_azure_voices_note"]     = "Voices are configured per language in the language pack (providerVoices.azure).",
            ["settings_track_proficiency"]     = "Track answers in proficiency",
            ["settings_proficiency_note"]      = "Off = practise without recording the result. Useful for debugging.",
            ["settings_about_text"]            = "LearnJP — adaptive Japanese vocabulary practice. Continuous testing across multiple proficiency criteria.",
            ["settings_ui_language"]           = "UI language",
            ["settings_ui_language_auto"]      = "Auto (System)",
        },

        ["de"] = new(StringComparer.Ordinal)
        {
            // Tab bar
            ["tab_language"]                   = "Sprache",
            ["tab_quiz"]                       = "Quiz",
            ["tab_mode"]                       = "Anpassen",
            ["tab_customize"]                  = "Anpassen",
            ["tab_progress"]                   = "Fortschritt",
            ["tab_settings"]                   = "Einstellungen",

            // Language selection page
            ["lang_base_language"]             = "Ausgangssprache",
            ["lang_active_target"]             = "Aktive Zielsprache",
            ["lang_select_button"]             = "Auswählen",
            ["lang_active_button"]             = "Aktiv",
            ["lang_learn_kana"]                = "Kana lernen",

            // Language names
            ["lang_english"]                   = "Englisch",
            ["lang_german"]                    = "Deutsch",
            ["lang_french"]                    = "Französisch",
            ["lang_spanish"]                   = "Spanisch",
            ["lang_italian"]                   = "Italienisch",
            ["lang_portuguese"]                = "Portugiesisch",
            ["lang_dutch"]                     = "Niederländisch",
            ["lang_russian"]                   = "Russisch",
            ["lang_chinese"]                   = "Chinesisch",
            ["lang_japanese"]                  = "Japanisch",
            ["lang_korean"]                    = "Koreanisch",

            // Quiz page
            ["quiz_hear_again"]                = "🔊  Nochmal hören",
            ["quiz_dont_know"]                 = "Ich weiß es nicht",
            ["quiz_skip"]                      = "Überspringen ▶",
            ["quiz_no_vocab"]                  = "Kein Vokabular geladen.",
            ["quiz_filter_prefix"]             = "Filter: ",

            // Progress page
            ["progress_total"]                 = "Gesamt",
            ["progress_seen"]                  = "Gesehen",
            ["progress_known"]                 = "Bekannt",
            ["progress_mastered"]              = "Gemeistert",
            ["progress_reset"]                 = "Fortschritt zurücksetzen",
            ["progress_search_placeholder"]    = "Begriffe suchen…",
            ["progress_reset_confirm_title"]   = "Fortschritt zurücksetzen",
            ["progress_reset_confirm_message"] = "Dadurch wird der gesamte Fortschritt dauerhaft gelöscht. Bist du sicher?",
            ["progress_reset_confirm_yes"]     = "Zurücksetzen",
            ["progress_reset_confirm_cancel"]  = "Abbrechen",
            ["progress_summary_format"]        = "{0}/{1} gemeistert · {2} bekannt · {3} gesehen",

            // Tag filter / mode page
            ["filter_strategy"]                = "Strategie",
            ["filter_no_filter"]               = "(kein Filter — alles löschen)",
            ["filter_active_no_filter"]        = "Kein Filter — aus dem gesamten Vokabular.",
            ["filter_active_prefix"]           = "Filter: ",
            ["filter_mode_auto"]               = "Auto-Progression",
            ["filter_mode_no_filter"]          = "Kein Filter",
            ["filter_mode_manual"]             = "Manuell",
            ["filter_active_auto_prefix"]      = "Auto: ",
            ["filter_progression_locked"]      = "Gesperrt",
            ["filter_progression_unlocked"]    = "Entsperrt",
            ["filter_progression_skipped"]     = "Übersprungen",

            // Customize tab
            ["customize_display_options"]      = "Spracheinstellungen",

            // Settings page — sections
            ["settings_display"]               = "Anzeige",
            ["settings_audio"]                 = "Audio",
            ["settings_behaviour"]             = "Verhalten",
            ["settings_about"]                 = "Über",
            ["settings_language"]              = "Sprache",

            // Settings page — fields
            ["settings_speak_question"]        = "Automatich sprache abspielen",
            ["settings_tts_provider"]          = "TTS-Anbieter",
            ["settings_system_volume"]         = "System-TTS-Lautstärke",
            ["settings_azure_volume"]          = "Azure-TTS-Lautstärke",
            ["settings_azure_key"]             = "Azure-Abonnementschlüssel",
            ["settings_azure_key_placeholder"] = "Schlüssel hier einfügen",
            ["settings_azure_region"]          = "Azure-Region",
            ["settings_azure_region_placeholder"] = "z.B. westeurope, eastus",
            ["settings_azure_voices_note"]     = "Stimmen werden pro Sprache im Sprachpaket konfiguriert (providerVoices.azure).",
            ["settings_track_proficiency"]     = "Antworten in der Kompetenz verfolgen",
            ["settings_proficiency_note"]      = "Aus = Üben ohne Ergebnis aufzuzeichnen. Nützlich zum Debuggen.",
            ["settings_about_text"]            = "LearnJP — adaptives japanisches Vokabelübungsprogramm. Kontinuierliches Testen nach mehreren Kompetenzkriterien.",
            ["settings_ui_language"]           = "Oberflächensprache",
            ["settings_ui_language_auto"]      = "Automatisch (System)",
        },
    };
}
