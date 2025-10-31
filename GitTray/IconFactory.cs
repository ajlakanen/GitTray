using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace GitTray;

/// <summary>
/// Factory for creating icons.
/// </summary>
public static class IconFactory
{
    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    public static Icon CreateCircleIcon(Color color)
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var brush = new SolidBrush(color);
            g.FillEllipse(brush, 2, 2, 28, 28);
            using var pen = new Pen(Color.Black, 2);
            g.DrawEllipse(pen, 2, 2, 28, 28);
        }

        var hIcon = bmp.GetHicon();
        var icon = Icon.FromHandle(hIcon);
        // clone to detach from HICON so we can free it
        var cloned = (Icon)icon.Clone();
        _ = DeleteObject(hIcon);
        icon.Dispose();
        return cloned;
    }
}