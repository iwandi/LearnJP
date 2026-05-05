namespace LearnJP.Models;

/// <summary>
/// Controls how the active word pool is filtered when generating quiz questions.
/// </summary>
public enum TagFilterMode
{
    /// <summary>
    /// Default. The progression ladder defined in the active <see cref="LanguagePack"/> is
    /// walked and only tags whose prerequisites have been sufficiently mastered are included.
    /// Falls back to the full vocabulary when the pack has no progression defined.
    /// </summary>
    AutoProgression,

    /// <summary>Draw from the full vocabulary (glyph tags still excluded by default).</summary>
    NoFilter,

    /// <summary>Use the explicitly configured include/exclude tag lists.</summary>
    Manual,
}
