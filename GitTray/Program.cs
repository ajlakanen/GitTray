namespace GitTray;

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