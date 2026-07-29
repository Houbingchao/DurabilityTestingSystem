using System.Drawing.Drawing2D;

namespace DurabilityTestingSystem.UI;

internal static class Theme
{
    public static readonly Color Window = Color.FromArgb(241, 245, 249);
    public static readonly Color Sidebar = Color.FromArgb(15, 31, 51);
    public static readonly Color SidebarMuted = Color.FromArgb(138, 158, 180);
    public static readonly Color Header = Color.White;
    public static readonly Color Card = Color.White;
    public static readonly Color Border = Color.FromArgb(218, 226, 235);
    public static readonly Color Text = Color.FromArgb(31, 48, 68);
    public static readonly Color Muted = Color.FromArgb(105, 123, 143);
    public static readonly Color Primary = Color.FromArgb(20, 106, 196);
    public static readonly Color PrimaryDark = Color.FromArgb(14, 77, 148);
    public static readonly Color PrimarySoft = Color.FromArgb(230, 241, 253);
    public static readonly Color Cyan = Color.FromArgb(0, 166, 196);
    public static readonly Color Green = Color.FromArgb(29, 165, 104);
    public static readonly Color GreenSoft = Color.FromArgb(228, 248, 239);
    public static readonly Color Orange = Color.FromArgb(239, 143, 39);
    public static readonly Color Red = Color.FromArgb(220, 68, 74);
    public static readonly Color RedSoft = Color.FromArgb(253, 235, 236);
    public static readonly Color Purple = Color.FromArgb(126, 87, 194);

    public static Font Font(float size, FontStyle style = FontStyle.Regular) =>
        new("Microsoft YaHei UI", size, style, GraphicsUnit.Point);

    public static GraphicsPath RoundedRect(Rectangle rect, int radius)
    {
        var path = new GraphicsPath();
        var d = Math.Max(1, radius * 2);
        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static void EnableDoubleBuffer(Control control)
    {
        typeof(Control).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic)?.SetValue(control, true);
    }
}
