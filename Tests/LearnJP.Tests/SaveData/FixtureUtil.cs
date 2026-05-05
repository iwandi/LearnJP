namespace LearnJP.Tests.SaveData;

/// <summary>
/// Resolves paths to test fixtures both in the runtime output (where xunit copies them to)
/// and in the source tree (where regenerate-the-fixture writes back).
/// </summary>
internal static class FixtureUtil
{
    /// <summary>Path to a fixture file as copied next to the test binaries at build time.</summary>
    public static string PathTo(string relative) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", relative);

    /// <summary>Path to a fixture file in the source tree — used when regenerating.</summary>
    public static string SourceTreePath(string relative)
    {
        // AppContext.BaseDirectory looks like
        //   ...\LearnJP\Tests\LearnJP.Tests\bin\Debug\net9.0\
        // climb back to the project folder.
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 4 && !File.Exists(Path.Combine(dir, "LearnJP.Tests.csproj")); i++)
            dir = Path.GetFullPath(Path.Combine(dir, ".."));
        return Path.Combine(dir, "Fixtures", relative);
    }
}
