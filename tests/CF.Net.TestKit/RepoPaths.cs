namespace CFNet.TestKit;

/// <summary>
/// Locates things that live in the repository rather than next to the test binaries.
/// </summary>
/// <remarks>
/// A test host can be started from anywhere - <c>dotnet test</c> at the solution root, a filtered run
/// inside one project, Visual Studio's runner - so <see cref="AppContext.BaseDirectory"/> is the only
/// reliable starting point. Walking up from there to <c>CF.Net.sln</c> works for all of them and for
/// a source checkout on any platform.
/// </remarks>
public static class RepoPaths
{
    /// <summary>Name of the solution file used as the repository marker.</summary>
    public const string SolutionFileName = "CF.Net.sln";

    /// <summary>Name of the directory cassettes are stored in.</summary>
    public const string CassettesDirectoryName = "cassettes";

    /// <summary>Name of the git-ignored file a developer can keep local credentials in.</summary>
    public const string LocalCredentialsFileName = "credentials.local.json";

    private static string? _cachedRoot;

    /// <summary>
    /// Walks up from <paramref name="start"/> (or the test binaries) until it finds
    /// <c>CF.Net.sln</c>.
    /// </summary>
    /// <param name="start">Optional starting directory; defaults to <see cref="AppContext.BaseDirectory"/>.</param>
    /// <returns>The absolute repository root.</returns>
    /// <exception cref="DirectoryNotFoundException">No solution file above the starting point.</exception>
    public static string FindRepositoryRoot(string? start = null)
    {
        if (_cachedRoot is not null && start is null)
        {
            return _cachedRoot;
        }

        var directory = new DirectoryInfo(start is null ? AppContext.BaseDirectory : Path.GetFullPath(start));

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, SolutionFileName)))
            {
                if (start is null)
                {
                    _cachedRoot = directory.FullName;
                }

                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            $"Could not find {SolutionFileName} at or above '{start ?? AppContext.BaseDirectory}'. " +
            "Cassette and credential lookups need the tests to run from inside a source checkout.");
    }

    /// <summary>Absolute path of the directory cassettes are committed to.</summary>
    public static string CassettesDirectory =>
        Path.Combine(FindRepositoryRoot(), "tests", CassettesDirectoryName);

    /// <summary>
    /// Absolute path of one cassette file. The name may carry its <c>.json</c> extension or not.
    /// </summary>
    /// <param name="name">Cassette name.</param>
    /// <returns>The full path.</returns>
    public static string CassettePath(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var fileName = name.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? name : name + ".json";
        return Path.Combine(CassettesDirectory, fileName);
    }

    /// <summary>
    /// Absolute path of the optional, git-ignored local credentials file:
    /// <c>tests/CF.Net.IntegrationTests/credentials.local.json</c>.
    /// </summary>
    public static string LocalCredentialsFile =>
        Path.Combine(FindRepositoryRoot(), "tests", "CF.Net.IntegrationTests", LocalCredentialsFileName);

    /// <summary>Every cassette file that exists, sorted by name.</summary>
    /// <returns>Full paths; empty when no cassette has been recorded yet.</returns>
    public static IReadOnlyList<string> DiscoverCassettes() => Cassette.Discover(CassettesDirectory);
}
