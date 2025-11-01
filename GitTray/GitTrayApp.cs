using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Timer = System.Windows.Forms.Timer;

namespace GitTray;

/// <summary>
/// Main application class managing the tray icon and scanning logic.
/// </summary>
public sealed class GitTrayApp : IDisposable
{
    private readonly Icon _redIcon;
    private readonly Icon _yellowIcon;
    private readonly Icon _greenIcon;
    private readonly ContextMenuStrip _menu;
    private readonly ToolStripMenuItem _exitItem;
    private readonly ToolStripMenuItem _openConfigItem;
    private readonly ToolStripMenuItem _openListItem;
    private readonly ToolStripMenuItem _rescanItem;
    private readonly Timer _timer;
    private readonly NotifyIcon _tray;

    private Config _config;
    private List<string> _dirty = new();
    private bool _isExiting;
    private DirtyListForm? _listForm;
    private List<RepoStatus> _statuses = new();
    private HashSet<string> _cachedRepos = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _lastDiscoveryTime = DateTime.MinValue;
    private List<FileSystemWatcher> _watchers = new();

    public GitTrayApp()
    {
        _config = Config.Load();

        _greenIcon = IconFactory.CreateCircleIcon(Color.LimeGreen);
        _yellowIcon = IconFactory.CreateCircleIcon(Color.Yellow);
        _redIcon = IconFactory.CreateCircleIcon(Color.Red);

        _menu = new ContextMenuStrip();
        _openListItem = new ToolStripMenuItem("Open list…", null, (_, __) => ShowListWindow());
        _rescanItem = new ToolStripMenuItem("Rescan now", null, async (_, __) => await RescanAsync());
        _openConfigItem = new ToolStripMenuItem("Open config.json", null, (_, __) => OpenConfig());
        _exitItem = new ToolStripMenuItem("Exit", null, (_, __) => Application.Exit());
        _menu.Items.AddRange(new ToolStripItem[]
        {
            _openListItem, _rescanItem, new ToolStripSeparator(), _openConfigItem, new ToolStripSeparator(), _exitItem
        });

        _tray = new NotifyIcon
        {
            Visible = true,
            Text = "GitTray: scanning…",
            ContextMenuStrip = _menu,
            Icon = _greenIcon
        };
        _tray.MouseClick += (s, e) =>
        {
            if (e.Button == MouseButtons.Left)
                ShowListWindow();
        };

        CreateListForm();

        // Setup timer to rescan periodically
        _timer = new Timer { Interval = Math.Max(5, _config.IntervalSeconds) * 1000 };
        _timer.Tick += async (_, __) => await RescanAsync();
        _timer.Start();

        // Initial repository discovery and watcher setup
        DiscoverRepos();
        SetupFileWatchers();

        // Initial scan
        _ = RescanAsync();

        // Update exit item to properly dispose list form
        _exitItem = new ToolStripMenuItem("Exit", null, (_, __) =>
        {
            _isExiting = true;
            _listForm?.Close(); // allow dispose
            Application.Exit();
        });
    }

    public void Dispose()
    {
        _timer.Dispose();
        _tray.Dispose();
        _greenIcon.Dispose();
        _redIcon.Dispose();
        _menu.Dispose();
        _listForm?.Dispose();
        foreach (var watcher in _watchers)
        {
            watcher.Dispose();
        }
    }

    /// <summary>
    ///     Creates the dirty list form and hooks up events.
    /// </summary>
    private void CreateListForm()
    {
        _listForm = new DirtyListForm();
        _listForm.OpenFolderRequested += OpenExplorer;
        _listForm.FormClosing += (s, e) =>
        {
            if (!_isExiting && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true; // don't dispose
                _listForm.Hide();
            }
        };
    }


    private void ShowListWindow()
    {
        // If the form was disposed, recreate it
        if (_listForm == null || _listForm.IsDisposed) CreateListForm();

        // Update list (we are sure _listForm is not null here) and show
        _listForm!.UpdateList(_statuses
            .Where(s => s.State != RepoState.Clean)
            .Select(s => FormatListRow(s)));
        if (!_listForm.Visible)
        {
            _listForm.StartPosition = FormStartPosition.Manual;
            var cursor = Control.MousePosition;
            _listForm.Location = new Point(cursor.X - _listForm.Width / 2, cursor.Y - _listForm.Height / 2);
            _listForm.Show();
        }
        else
        {
            _listForm.Activate();
        }
    }


    private static string FormatListRow(RepoStatus s)
    {
        var tag = s.State switch
        {
            RepoState.Dirty => "DIRTY",
            RepoState.AheadOnly => $"AHEAD +{s.Ahead}",
            RepoState.BehindOnly => $"BEHIND -{s.Behind}",
            RepoState.Diverged => $"DIV +{s.Ahead}/-{s.Behind}",
            RepoState.NoUpstream => "NO-UPSTREAM",
            _ => ""
        };
        return string.IsNullOrEmpty(tag) ? s.Path : $"[{tag}] {s.Path}";
    }


    private void OpenConfig()
    {
        var path = Config.Path;
        if (!File.Exists(path))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path,
                JsonSerializer.Serialize(Config.Default(), new JsonSerializerOptions { WriteIndented = true }));
        }

        Process.Start(new ProcessStartInfo("notepad.exe", path) { UseShellExecute = false });
    }

    private static void OpenExplorer(string dir)
    {
        if (Directory.Exists(dir))
            Process.Start(new ProcessStartInfo("explorer.exe", dir) { UseShellExecute = true });
    }

    /// <summary>
    /// Discover all Git repositories in the configured roots.
    /// </summary>
    private void DiscoverRepos()
    {
        _cachedRepos = new HashSet<string>(
            RepoFinder.FindRepos(_config.Roots, _config.IgnorePatterns), 
            StringComparer.OrdinalIgnoreCase);
        _lastDiscoveryTime = DateTime.UtcNow;
    }

    /// <summary>
    /// Setup FileSystemWatchers to detect new .git directories.
    /// </summary>
    private void SetupFileWatchers()
    {
        // Dispose existing watchers
        foreach (var watcher in _watchers)
        {
            watcher.Dispose();
        }
        _watchers.Clear();

        // Create new watchers for each root
        foreach (var root in _config.Roots)
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) continue;

            try
            {
                var watcher = new FileSystemWatcher(root)
                {
                    Filter = ".git",
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.DirectoryName
                };

                watcher.Created += OnGitDirCreated;
                watcher.EnableRaisingEvents = true;
                _watchers.Add(watcher);
            }
            catch
            {
                // Ignore errors setting up watchers (e.g., network drives, permissions)
            }
        }
    }

    /// <summary>
    /// Handle new .git directory creation.
    /// </summary>
    private void OnGitDirCreated(object sender, FileSystemEventArgs e)
    {
        try
        {
            // Check if this is a .git directory
            if (Path.GetFileName(e.FullPath).Equals(".git", StringComparison.OrdinalIgnoreCase))
            {
                var repoPath = Path.GetDirectoryName(e.FullPath);
                if (!string.IsNullOrEmpty(repoPath) && !RepoFinder.IsIgnored(repoPath, _config.IgnorePatterns))
                {
                    lock (_cachedRepos)
                    {
                        _cachedRepos.Add(repoPath);
                    }
                }
            }
        }
        catch
        {
            // Ignore errors in watcher callback
        }
    }

    public async Task RescanAsync()
    {
        try
        {
            _config = Config.Load();
            
            // Check if we need to rediscover repositories
            var now = DateTime.UtcNow;
            var discoveryInterval = TimeSpan.FromMinutes(_config.RepoDiscoveryMinutes);
            if (now - _lastDiscoveryTime >= discoveryInterval)
            {
                DiscoverRepos();
                SetupFileWatchers(); // Refresh watchers in case roots changed
            }
            
            // Use cached repos for status checking (create a snapshot to avoid holding lock during async operation)
            IEnumerable<string> reposSnapshot;
            lock (_cachedRepos)
            {
                reposSnapshot = _cachedRepos.ToList();
            }
            _statuses = (await GitChecker.GetStatusesAsync(reposSnapshot))
                .OrderBy(s => s.Path, StringComparer.OrdinalIgnoreCase).ToList();

            var anyDirty = _statuses.Any(s =>
                s.State == RepoState.Dirty || s.State == RepoState.BehindOnly || s.State == RepoState.Diverged);
            var anyAhead = _statuses.Any(s => s.State == RepoState.AheadOnly);
            var anyNoUpstream = _statuses.Any(s => s.State == RepoState.NoUpstream);

            // Red beats yellow; yellow if ahead OR no-upstream; otherwise green
            _tray.Icon = anyDirty ? _redIcon : anyAhead || anyNoUpstream ? _yellowIcon : _greenIcon;
            _tray.Text = BuildTooltip(_statuses);

            _listForm?.UpdateList(_statuses
                .Where(s => s.State != RepoState.Clean)
                .Select(s => FormatListRow(s)));
        }
        catch (Exception ex)
        {
            _tray.Icon = _redIcon;
            _tray.Text = $"GitTray error: {ex.Message}";
        }
    }

    private static string BuildTooltip(List<RepoStatus> statuses)
    {
        var dirty = statuses.Count(s => s.State == RepoState.Dirty);
        var ahead = statuses.Count(s => s.State == RepoState.AheadOnly);
        var behind = statuses.Count(s => s.State == RepoState.BehindOnly || s.State == RepoState.Diverged);
        var noUp = statuses.Count(s => s.State == RepoState.NoUpstream);

        if (dirty == 0 && ahead == 0 && behind == 0 && noUp == 0) return "GitTray: All repos clean";

        var sb = new StringBuilder();
        if (dirty > 0) sb.Append($"Dirty: {dirty}. ");
        if (ahead > 0) sb.Append($"Unpushed: {ahead}. ");
        if (behind > 0) sb.Append($"Behind: {behind}. ");
        if (noUp > 0) sb.Append($"No Upstream: {noUp}. ");
        var lines = statuses
            .Where(s => s.State != RepoState.Clean && s.State != RepoState.NoUpstream)
            .Take(3)
            .Select(s => "• " + Path.GetFileName(s.Path));
        // var text = (sb.ToString() + " " + string.Join(" ", lines)).Trim();
        var text = sb + Environment.NewLine + string.Join(Environment.NewLine, lines);
        return text.Length > 120 ? text.Substring(0, 119) : text;
    }
}