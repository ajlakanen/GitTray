namespace GitTray;

/// <summary>
/// Form displaying the list of dirty repositories.
/// </summary>
public sealed class DirtyListForm : Form
{
    /// <summary>
    /// List box showing dirty repositories.
    /// </summary>
    private readonly ListBox _list = new() { Dock = DockStyle.Fill, IntegralHeight = false };

    /// <summary>
    /// Button to open the selected repository in Explorer.
    /// </summary>
    private readonly Button _openBtn = new() { Text = "Open in Explorer", Dock = DockStyle.Bottom, Height = 36 };

    /// <summary>
    /// Button to refresh the list.
    /// </summary>
    private readonly Button _refreshBtn = new() { Text = "Refresh", Dock = DockStyle.Bottom, Height = 30 };

    /// <summary>
    /// Initialize DirtyListForm
    /// </summary>
    public DirtyListForm()
    {
        Text = "Dirty Git repositories";
        Width = 520;
        Height = 420;
        Controls.Add(_list);
        Controls.Add(_openBtn);
        Controls.Add(_refreshBtn);
        _openBtn.Click += (_, __) =>
        {
            if (_list.SelectedItem is string s) OpenFolderRequested?.Invoke(s);
        };
        _refreshBtn.Click += (_, __) =>
        {
            /* the app refreshes itself; this just nudges */
            Hide();
            Show();
        };
        _list.DoubleClick += (_, __) =>
        {
            if (_list.SelectedItem is string s) OpenFolderRequested?.Invoke(s);
        };
    }

    /// <summary>
    /// Occurs when a folder should be opened.
    /// </summary>
    public event Action<string>? OpenFolderRequested;

    /// <summary>
    /// Update the list of dirty repositories.
    /// </summary>
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