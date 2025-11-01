namespace GitTray;

/// <summary>
/// Finder for Git repositories.
/// </summary>
public static class RepoFinder
{
    /// <summary>
    /// Find Git repositories under the given root directories, excluding those that match ignore patterns.
    /// </summary>
    /// <param name="roots">Root directories to search.</param>
    /// <param name="ignorePatterns">Ignore patterns.</param>
    /// <returns>Found repository paths.</returns>
    public static IEnumerable<string> FindRepos(IEnumerable<string> roots, IEnumerable<string> ignorePatterns)
    {
        var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots)
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) continue;
            foreach (var gitDir in Directory.EnumerateDirectories(root, ".git", SearchOption.AllDirectories))
            {
                var repo = Path.GetDirectoryName(gitDir)!;
                if (IsIgnored(repo, ignorePatterns)) continue;
                results.Add(repo);
            }
        }

        return results;
    }


    /// <summary>
    /// Check if the given path matches any of the ignore patterns.
    /// </summary>
    /// <param name="path">The path to check.</param>
    /// <param name="patterns">The ignore patterns.</param>
    /// <returns>True if the path should be ignored; otherwise false.</returns>
    public static bool IsIgnored(string path, IEnumerable<string> patterns)
    {
        foreach (var p in patterns)
        {
            if (string.IsNullOrWhiteSpace(p)) continue;
            // simple contains/EndsWith matching. Keep it fast and dependency-free.
            if (p.Contains('*'))
            {
                // very lightweight wildcard: support leading/trailing * only
                var core = p.Trim('*');
                if (p.StartsWith("*") && p.EndsWith("*"))
                {
                    if (path.Contains(core, StringComparison.OrdinalIgnoreCase)) return true;
                }
                else if (p.StartsWith("*"))
                {
                    if (path.EndsWith(core, StringComparison.OrdinalIgnoreCase)) return true;
                }
                else if (p.EndsWith("*"))
                {
                    if (path.StartsWith(core, StringComparison.OrdinalIgnoreCase)) return true;
                }
            }
            else
            {
                if (path.Contains(p, StringComparison.OrdinalIgnoreCase)) return true;
            }
        }

        return false;
    }
}