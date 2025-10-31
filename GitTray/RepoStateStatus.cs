namespace GitTray;

public enum RepoState
{
    Clean,
    Dirty,
    AheadOnly,
    BehindOnly,
    Diverged,
    NoUpstream,
    Error
}

public sealed record RepoStatus(string Path, RepoState State, int Ahead = 0, int Behind = 0);