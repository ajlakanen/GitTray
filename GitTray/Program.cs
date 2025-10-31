using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Forms;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        using var app = new GitTrayApp();
        Application.Run();
    }
}

public sealed class GitTrayApp : IDisposable
{
    private readonly NotifyIcon _tray;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly Icon _greenIcon;
    private readonly Icon _yellowIcon;
    private readonly Icon _redIcon;
    private readonly ContextMenuStrip _menu;
    private readonly ToolStripMenuItem _openListItem;
    private readonly ToolStripMenuItem _rescanItem;
    private readonly ToolStripMenuItem _openConfigItem;
    private readonly ToolStripMenuItem _exitItem;
    private DirtyListForm? _listForm;
    private bool _isExiting;

    private Config _config;
    private List<string> _dirty = new();
    private List<RepoStatus> _statuses = new();

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
        _menu.Items.AddRange(new ToolStripItem[] { _openListItem, _rescanItem, new ToolStripSeparator(), _openConfigItem, new ToolStripSeparator(), _exitItem });

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
        _timer = new System.Windows.Forms.Timer { Interval = Math.Max(5, _config.IntervalSeconds) * 1000 };
        _timer.Tick += async (_, __) => await RescanAsync();
        _timer.Start();

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

    /// <summary>
    /// Creates the dirty list form and hooks up events.
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
             .Where(s => s.State != RepoState.Clean && s.State != RepoState.NoUpstream)
             .Select(s => FormatListRow(s)));
        if (!_listForm.Visible)
        {
            _listForm.StartPosition = FormStartPosition.Manual;
            var cursor = Control.MousePosition;
            _listForm.Location = new Point(cursor.X - _listForm.Width / 2, cursor.Y - _listForm.Height / 2);
            _listForm.Show();
        }
        else _listForm.Activate();
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
            File.WriteAllText(path, JsonSerializer.Serialize(Config.Default(), new JsonSerializerOptions { WriteIndented = true }));
        }
        Process.Start(new ProcessStartInfo("notepad.exe", path) { UseShellExecute = false });
    }

    private static void OpenExplorer(string dir)
    {
        if (Directory.Exists(dir))
            Process.Start(new ProcessStartInfo("explorer.exe", dir) { UseShellExecute = true });
    }

    public async Task RescanAsync()
    {
        try
        {
            _config = Config.Load();
            var repos = RepoFinder.FindRepos(_config.Roots, _config.IgnorePatterns);
            _statuses = (await GitChecker.GetStatusesAsync(repos))
                .OrderBy(s => s.Path, StringComparer.OrdinalIgnoreCase).ToList();

            var anyDirty = _statuses.Any(s => s.State == RepoState.Dirty || s.State == RepoState.BehindOnly || s.State == RepoState.Diverged);
            var anyAhead = _statuses.Any(s => s.State == RepoState.AheadOnly);

            _tray.Icon = anyDirty ? _redIcon : anyAhead ? _yellowIcon : _greenIcon;
            _tray.Text = BuildTooltip(_statuses);

            _listForm?.UpdateList(_statuses
                .Where(s => s.State != RepoState.Clean && s.State != RepoState.NoUpstream)
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
        if (dirty == 0 && ahead == 0 && behind == 0) return "GitTray: All repos clean";

        var sb = new StringBuilder();
        if (dirty > 0) sb.Append($"Dirty: {dirty}. ");
        if (ahead > 0) sb.Append($"Unpushed: {ahead}. ");
        if (behind > 0) sb.Append($"Behind: {behind}. ");
        var lines = statuses
            .Where(s => s.State != RepoState.Clean && s.State != RepoState.NoUpstream)
            .Take(3)
            .Select(s => "• " + System.IO.Path.GetFileName(s.Path));
        // var text = (sb.ToString() + " " + string.Join(" ", lines)).Trim();
        var text = sb.ToString() + Environment.NewLine + string.Join(Environment.NewLine, lines);
        return text.Length > 120 ? text.Substring(0, 119) : text;
    }

    public void Dispose()
    {
        _timer.Dispose();
        _tray.Dispose();
        _greenIcon.Dispose();
        _redIcon.Dispose();
        _menu.Dispose();
        _listForm.Dispose();
    }

    
}



public static class RepoFinder
{
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

    private static bool IsIgnored(string path, IEnumerable<string> patterns)
    {
        foreach (var p in patterns)
        {
            if (string.IsNullOrWhiteSpace(p)) continue;
            // simple contains/EndsWith matching. Keep it fast and dependency-free.
            if (p.Contains('*'))
            {
                // very lightweight wildcard: support leading/trailing * only
                var core = p.Trim('*');
                if (p.StartsWith("*") && p.EndsWith("*")) { if (path.Contains(core, StringComparison.OrdinalIgnoreCase)) return true; }
                else if (p.StartsWith("*")) { if (path.EndsWith(core, StringComparison.OrdinalIgnoreCase)) return true; }
                else if (p.EndsWith("*")) { if (path.StartsWith(core, StringComparison.OrdinalIgnoreCase)) return true; }
            }
            else
            {
                if (path.Contains(p, StringComparison.OrdinalIgnoreCase)) return true;
            }
        }
        return false;
    }
}


public enum RepoState { Clean, Dirty, AheadOnly, BehindOnly, Diverged, NoUpstream, Error }

public sealed record RepoStatus(string Path, RepoState State, int Ahead = 0, int Behind = 0);


public static class GitChecker
{
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
                    lock (list) list.Add(new RepoStatus(repo, RepoState.Dirty));
                    return;
                }

                // 2) Branch tracking / ahead-behind using porcelain v2
                var (exit2, out2) = await RunGitAsync(repo, "status -sb --porcelain=2 -b");
                if (exit2 != 0)
                {
                    lock (list) list.Add(new RepoStatus(repo, RepoState.Error));
                    return;
                }

                // Example line: "# branch.ab +2 -0" if upstream set
                // If there's no upstream, porcelain v2 typically includes "# branch.upstream origin/main" missing or branch.ab missing.
                int ahead = 0, behind = 0; bool hasUpstream = false;
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
                    lock (list) list.Add(new RepoStatus(repo, RepoState.NoUpstream));
                    return;
                }

                RepoState state = RepoState.Clean;
                if (ahead > 0 && behind == 0) state = RepoState.AheadOnly;
                else if (behind > 0 && ahead == 0) state = RepoState.BehindOnly;
                else if (ahead > 0 && behind > 0) state = RepoState.Diverged;

                lock (list) list.Add(new RepoStatus(repo, state, ahead, behind));
            }
            catch
            {
                lock (list) list.Add(new RepoStatus(repo, RepoState.Error));
            }
        });
        await Task.WhenAll(tasks);
        return list;
    }

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
        using var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var sb = new StringBuilder();
        var tcs = new TaskCompletionSource<int>();
        p.OutputDataReceived += (_, e) => { if (e.Data != null) sb.AppendLine(e.Data); };
        p.ErrorDataReceived += (_, __) => { /* ignore */ };
        p.Exited += (_, __) => tcs.TrySetResult(p.ExitCode);
        p.Start();
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        var exit = await tcs.Task.ConfigureAwait(false);
        return (exit, sb.ToString());
    }
}

public static class IconFactory
{
    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    public static Icon CreateCircleIcon(Color color)
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var brush = new SolidBrush(color);
            g.FillEllipse(brush, 2, 2, 28, 28);
            using var pen = new Pen(Color.Black, 2);
            g.DrawEllipse(pen, 2, 2, 28, 28);
        }
        IntPtr hIcon = bmp.GetHicon();
        var icon = Icon.FromHandle(hIcon);
        // clone to detach from HICON so we can free it
        var cloned = (Icon)icon.Clone();
        _ = DeleteObject(hIcon);
        icon.Dispose();
        return cloned;
    }
}

public sealed class DirtyListForm : Form
{
    private readonly ListBox _list = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly Button _openBtn = new() { Text = "Open in Explorer", Dock = DockStyle.Bottom, Height = 36 };
    private readonly Button _refreshBtn = new() { Text = "Refresh", Dock = DockStyle.Bottom, Height = 30 };

    public event Action<string>? OpenFolderRequested;

    public DirtyListForm()
    {
        Text = "Dirty Git repositories";
        Width = 520; Height = 420;
        Controls.Add(_list);
        Controls.Add(_openBtn);
        Controls.Add(_refreshBtn);
        _openBtn.Click += (_, __) => { if (_list.SelectedItem is string s) OpenFolderRequested?.Invoke(s); };
        _refreshBtn.Click += (_, __) => { /* the app refreshes itself; this just nudges */ this.Hide(); this.Show(); };
        _list.DoubleClick += (_, __) => { if (_list.SelectedItem is string s) OpenFolderRequested?.Invoke(s); };
    }

    public void UpdateList(IEnumerable<string> items)
    {
        var sel = _list.SelectedItem as string;
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var it in items) _list.Items.Add(it);
        if (sel != null && items.Contains(sel)) _list.SelectedItem = sel;
        _list.EndUpdate();
    }
}

public sealed class Config
{
    [JsonPropertyName("roots")] public List<string> Roots { get; set; } = new();
    [JsonPropertyName("ignorePatterns")] public List<string> IgnorePatterns { get; set; } = new();
    [JsonPropertyName("intervalSeconds")] public int IntervalSeconds { get; set; } = 60;

    public static string Path => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GitTray", "config.json");

    public static Config Load()
    {
        try
        {
            if (File.Exists(Path))
            {
                var cfg = JsonSerializer.Deserialize<Config>(File.ReadAllText(Path));
                if (cfg != null && cfg.Roots.Count > 0) return cfg;
            }
        }
        catch { /* fall through to default */ }
        return Default();
    }

    public static Config Default()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var defaultRoots = new List<string>();
        var common = new[] { System.IO.Path.Combine(userProfile, "source"), System.IO.Path.Combine(userProfile, "Documents"), Environment.CurrentDirectory };
        foreach (var r in common) if (Directory.Exists(r)) defaultRoots.Add(r);
        return new Config
        {
            Roots = defaultRoots.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            IgnorePatterns = new List<string> { "\\.git\\modules\\*", "node_modules", "bin\\*", "obj\\*" },
            IntervalSeconds = 60
        };
    }
}
