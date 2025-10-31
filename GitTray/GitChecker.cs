using System.Diagnostics;
using System.Text;

namespace GitTray;

/// <summary>
/// Class for checking Git repository statuses.
/// </summary>
public static class GitChecker
{
    /// <summary>
    /// Get the statuses of the given repositories.
    /// </summary>
    public static async Task<List<RepoStatus>> GetStatusesAsync(IEnumerable<string> repos)
    {

        var list = new List<RepoStatus>();
        var tasks = repos.Select(async repo =>
        {
            try
            {
                // 1) Is working tree dirty?
                var (exit1, out1) = await RunGitAsync(repo, "status --porcelain");
                if (exit1 == 0 && !string.IsNullOrWhiteSpace(out1))
                {
                    lock (list)
                    {
                        list.Add(new RepoStatus(repo, RepoState.Dirty));
                    }

                    return;
                }

                // 2) Branch tracking / ahead-behind using porcelain v2
                var (exit2, out2) = await RunGitAsync(repo, "status -sb --porcelain=2 -b");
                if (exit2 != 0)
                {
                    lock (list)
                    {
                        list.Add(new RepoStatus(repo, RepoState.Error));
                    }

                    return;
                }

                // Example line: "# branch.ab +2 -0" if upstream set
                // If there's no upstream, porcelain v2 typically includes "# branch.upstream origin/main" missing or branch.ab missing.
                int ahead = 0, behind = 0;
                var hasUpstream = false;
                foreach (var line in out2.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var ln = line.Trim();
                    if (ln.StartsWith("# branch.ab"))
                    {
                        hasUpstream = true;
                        // parse +A -B
                        var parts = ln.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        // parts: [#, branch.ab, +A, -B]
                        if (parts.Length >= 4)
                        {
                            int.TryParse(parts[2].TrimStart('+'), out ahead);
                            int.TryParse(parts[3].TrimStart('-'), out behind);
                        }

                        break;
                    }
                }

                if (!hasUpstream)
                {
                    lock (list)
                    {
                        list.Add(new RepoStatus(repo, RepoState.NoUpstream));
                    }

                    return;
                }

                var state = RepoState.Clean;
                if (ahead > 0 && behind == 0) state = RepoState.AheadOnly;
                else if (behind > 0 && ahead == 0) state = RepoState.BehindOnly;
                else if (ahead > 0 && behind > 0) state = RepoState.Diverged;

                lock (list)
                {
                    list.Add(new RepoStatus(repo, state, ahead, behind));
                }
            }
            catch
            {
                lock (list)
                {
                    list.Add(new RepoStatus(repo, RepoState.Error));
                }
            }
        });
        await Task.WhenAll(tasks);
        return list;
    }

    /// <summary>
    /// Run a Git command asynchronously.
    /// </summary>
    private static async Task<(int exitCode, string output)> RunGitAsync(string workingDir, string args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = args,
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var p = new Process();
        p.StartInfo = psi;
        p.EnableRaisingEvents = true;
        var sb = new StringBuilder();
        var tcs = new TaskCompletionSource<int>();
        p.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null) sb.AppendLine(e.Data);
        };
        p.ErrorDataReceived += (_, __) =>
        {
            // ignore errors?
        };
        // When the process exits, set the result
        p.Exited += (_, __) => tcs.TrySetResult(p.ExitCode);
        p.Start();
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();

        // Wait for exit
        var exit = await tcs.Task.ConfigureAwait(false);
        return (exit, sb.ToString());
    }
}