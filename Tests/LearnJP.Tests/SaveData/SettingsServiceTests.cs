using LearnJP.Models;
using LearnJP.Services;
using Xunit;

namespace LearnJP.Tests.SaveData;

/// <summary>
/// Save-data integrity for <see cref="SettingsService"/>. Three concerns:
///
/// <list type="number">
///   <item><b>Roundtrip</b> — every property writes a value and reads it back unchanged.</item>
///   <item><b>Key stability</b> — the strings used as preference keys are pinned. Renaming
///         one would silently lose the user's prior value. (Stored values become "default"
///         the next time the app runs.)</item>
///   <item><b>Enum-int pinning</b> — enums backed by an int in storage have their numeric
///         values asserted. If anyone reorders <see cref="LearningStrategy"/> etc., every
///         existing user gets a different setting on next launch.</item>
/// </list>
/// </summary>
public sealed class SettingsServiceTests
{
    private static (SettingsService settings, InMemoryPreferenceBackend backend) NewService()
    {
        var backend = new InMemoryPreferenceBackend();
        return (new SettingsService(backend), backend);
    }

    // --- Roundtrip ---------------------------------------------------------

    [Fact]
    public void Roundtrip_AllSimpleProperties()
    {
        var (s, _) = NewService();

        s.TtsEnabled = false;
        Assert.False(s.TtsEnabled);

        s.TtsRate = 1.25;
        Assert.Equal(1.25, s.TtsRate);

        s.TtsProvider = TtsProvider.System;
        Assert.Equal(TtsProvider.System, s.TtsProvider);

        s.AzureSpeechKey = "the-key";
        Assert.Equal("the-key", s.AzureSpeechKey);

        s.AzureSpeechRegion = "northeurope";
        Assert.Equal("northeurope", s.AzureSpeechRegion);

        s.SystemTtsVolume = 0.4;
        Assert.Equal(0.4, s.SystemTtsVolume);

        s.AzureTtsVolume = 0.7;
        Assert.Equal(0.7, s.AzureTtsVolume);

        s.TagFilterMode = TagFilterMode.Manual;
        Assert.Equal(TagFilterMode.Manual, s.TagFilterMode);

        s.SelectedLearningStrategy = LearningStrategy.WeakFocus;
        Assert.Equal(LearningStrategy.WeakFocus, s.SelectedLearningStrategy);

        s.CountForProficiency = false;
        Assert.False(s.CountForProficiency);

        s.ActiveLanguageId = "ja";
        Assert.Equal("ja", s.ActiveLanguageId);

        s.BaseLanguageId = "de";
        Assert.Equal("de", s.BaseLanguageId);

        s.UiLanguageOverride = "fr";
        Assert.Equal("fr", s.UiLanguageOverride);
    }

    [Fact]
    public void Roundtrip_TagLists()
    {
        var (s, _) = NewService();

        s.ActiveIncludeTags = new[] { "n5", "n4" };
        s.ActiveExcludeTags = new[] { "kanji-only" };

        Assert.Equal(new[] { "n5", "n4" },     s.ActiveIncludeTags);
        Assert.Equal(new[] { "kanji-only" },   s.ActiveExcludeTags);
    }

    [Fact]
    public void Roundtrip_TagLists_TrimAndDropEmpty()
    {
        // Trim+drop-empty behaviour on encode: load-bearing because users / migration code
        // that pastes whitespace shouldn't produce blank tags.
        var (s, _) = NewService();
        s.ActiveIncludeTags = new[] { " a ", "", "b" };
        Assert.Equal(new[] { "a", "b" }, s.ActiveIncludeTags);
    }

    [Fact]
    public void Roundtrip_TagLists_PreservesCommas()
    {
        // The pre-JSON encoder joined with "," and silently corrupted any tag containing a
        // comma. Pin the new behaviour: tags survive round-trip with their bytes intact.
        var (s, _) = NewService();
        s.ActiveIncludeTags = new[] { "hello, world", "fine" };
        Assert.Equal(new[] { "hello, world", "fine" }, s.ActiveIncludeTags);
    }

    [Fact]
    public void DecodeTagList_AcceptsLegacyCommaFormat()
    {
        // Existing users have settings.include_tags stored as bare "n5,n4" strings — the
        // JSON-aware decoder must still read them, otherwise the upgrade loses tags.
        var legacyDecoded = SettingsService.DecodeTagList("n5,n4, n3 ");
        Assert.Equal(new[] { "n5", "n4", "n3" }, legacyDecoded);
    }

    [Fact]
    public void DecodeTagList_AcceptsJsonFormat()
    {
        var jsonDecoded = SettingsService.DecodeTagList("[\"n5\",\"n4\"]");
        Assert.Equal(new[] { "n5", "n4" }, jsonDecoded);
    }

    [Fact]
    public void EncodeTagList_EmitsJson()
    {
        var encoded = SettingsService.EncodeTagList(new[] { "a", "b" });
        Assert.StartsWith("[", encoded);
        // Round-trip through System.Text.Json directly to prove the encoder produces
        // canonical JSON, not just any string the decoder happens to accept.
        var arr = System.Text.Json.JsonSerializer.Deserialize<string[]>(encoded);
        Assert.Equal(new[] { "a", "b" }, arr);
    }

    [Fact]
    public void Roundtrip_DisplayFlags_AreScopedPerPack()
    {
        var (s, _) = NewService();

        s.SetDisplayFlag(packId: "ja", key: "always-furigana", value: true);
        s.SetDisplayFlag(packId: "it", key: "always-furigana", value: false);

        Assert.True(s.GetDisplayFlag("ja", "always-furigana", defaultValue: false));
        Assert.False(s.GetDisplayFlag("it", "always-furigana", defaultValue: true));
        // A new pack id sees the default.
        Assert.True(s.GetDisplayFlag("ko", "always-furigana", defaultValue: true));
    }

    [Fact]
    public void Volumes_AreClamped_OnWrite_AndOnRead()
    {
        var (s, backend) = NewService();

        s.SystemTtsVolume = 2.0;
        Assert.Equal(1.0, s.SystemTtsVolume);

        s.AzureTtsVolume = -0.5;
        Assert.Equal(0.0, s.AzureTtsVolume);

        // Even if a corrupted preference is read back as out-of-range, the getter clamps.
        backend.Set("settings.system_volume", 3.0);
        Assert.Equal(1.0, s.SystemTtsVolume);
    }

    [Fact]
    public void Defaults_AreStable_WhenNothingPersisted()
    {
        var (s, _) = NewService();
        // These defaults are part of the save-data contract: a fresh install should land on
        // exactly these values. If someone changes a default, this test catches it and we
        // can decide whether the change is intentional.
        Assert.True(s.TtsEnabled);
        Assert.Equal(0.9, s.TtsRate);
        Assert.Equal(TtsProvider.Azure, s.TtsProvider);
        Assert.Equal(string.Empty, s.AzureSpeechKey);
        Assert.Equal("westeurope", s.AzureSpeechRegion);
        Assert.Equal(1.0, s.SystemTtsVolume);
        Assert.Equal(1.0, s.AzureTtsVolume);
        Assert.Empty(s.ActiveIncludeTags);
        Assert.Empty(s.ActiveExcludeTags);
        Assert.Equal(TagFilterMode.AutoProgression, s.TagFilterMode);
        Assert.Equal(LearningStrategy.Fsrs, s.SelectedLearningStrategy);
        Assert.True(s.CountForProficiency);
        Assert.Equal(string.Empty, s.ActiveLanguageId);
        Assert.Equal("en", s.BaseLanguageId);
        Assert.Equal(string.Empty, s.UiLanguageOverride);
    }

    // --- Key stability -----------------------------------------------------

    [Fact]
    public void KeyStrings_ArePinned()
    {
        // The strings the SettingsService writes to in Preferences ARE the save format —
        // renaming one orphans every user's prior value. Setting each property and then
        // reading the backend's snapshot tells us, by construction, which key was used.
        var (s, backend) = NewService();

        s.TtsEnabled              = false;
        s.TtsRate                 = 1.0;
        s.TtsProvider             = TtsProvider.System;
        s.AzureSpeechKey          = "k";
        s.AzureSpeechRegion       = "r";
        s.SystemTtsVolume         = 0.5;
        s.AzureTtsVolume          = 0.5;
        s.ActiveIncludeTags       = new[] { "a" };
        s.ActiveExcludeTags       = new[] { "b" };
        s.TagFilterMode           = TagFilterMode.Manual;
        s.SelectedLearningStrategy = LearningStrategy.WeakFocus;
        s.CountForProficiency     = false;
        s.ActiveLanguageId        = "ja";
        s.BaseLanguageId          = "de";
        s.UiLanguageOverride      = "en";
        s.SetDisplayFlag("ja", "always-furigana", true);

        var keys = backend.Snapshot().Keys.ToHashSet();

        Assert.Contains("settings.tts_enabled",         keys);
        Assert.Contains("settings.tts_rate",            keys);
        Assert.Contains("settings.tts_provider",        keys);
        Assert.Contains("settings.azure_key",           keys);
        Assert.Contains("settings.azure_region",        keys);
        Assert.Contains("settings.system_volume",       keys);
        Assert.Contains("settings.azure_volume",        keys);
        Assert.Contains("settings.include_tags",        keys);
        Assert.Contains("settings.exclude_tags",        keys);
        Assert.Contains("settings.tag_filter_mode",     keys);
        Assert.Contains("settings.learning_strategy",   keys);
        Assert.Contains("settings.count_for_proficiency", keys);
        Assert.Contains("settings.active_language_id",  keys);
        Assert.Contains("settings.base_language_id",    keys);
        Assert.Contains("settings.ui_language_override", keys);
        Assert.Contains("settings.display.ja.always-furigana", keys);
    }

    // --- Enum-int pinning --------------------------------------------------

    /// <summary>
    /// Enums persisted as <c>int</c> have their numeric values pinned. Reordering a member
    /// is a save-data break: every existing user's stored int now points at a different
    /// member. If you must add a new enum value, append it at the end so existing ints
    /// stay valid.
    /// </summary>
    [Fact]
    public void EnumIntegerValues_ArePinned()
    {
        Assert.Equal(0, (int)TtsProvider.System);
        Assert.Equal(1, (int)TtsProvider.Azure);

        Assert.Equal(0, (int)TagFilterMode.AutoProgression);
        Assert.Equal(1, (int)TagFilterMode.NoFilter);
        Assert.Equal(2, (int)TagFilterMode.Manual);

        Assert.Equal(0, (int)LearningStrategy.Neutral);
        Assert.Equal(1, (int)LearningStrategy.Spaced);
        Assert.Equal(2, (int)LearningStrategy.QuickReview);
        Assert.Equal(3, (int)LearningStrategy.WeakFocus);
        Assert.Equal(4, (int)LearningStrategy.Fsrs);

        // ProficiencyCriterion is stored as int in the SQLite proficiency_scores.criterion
        // column — same break risk applies.
        Assert.Equal(0, (int)ProficiencyCriterion.TargetToBase);
        Assert.Equal(1, (int)ProficiencyCriterion.BaseToTarget);
        Assert.Equal(2, (int)ProficiencyCriterion.SimilarSoundDifferentiation);
        Assert.Equal(3, (int)ProficiencyCriterion.SimilarMeaningDifferentiation);
    }

    [Fact]
    public void EnumIntegerValues_DoNotShift_OnRoundTrip()
    {
        var (s, backend) = NewService();
        s.SelectedLearningStrategy = LearningStrategy.QuickReview;
        // Whatever the strategy enum's int value is, the backend stores it as an int.
        // If a future refactor switched to writing the name as a string, this fails.
        Assert.IsType<int>(backend.Snapshot()["settings.learning_strategy"]);
        Assert.Equal(2, (int)backend.Snapshot()["settings.learning_strategy"]);
    }
}
