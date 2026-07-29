using System.Drawing.Drawing2D;

namespace DurabilityTestingSystem.UI.Controls;

/// <summary>
/// Compact brand mark: an abstract seated occupant crossed by a safety belt.
/// It is drawn as vectors so it remains crisp on high-DPI industrial displays.
/// </summary>
public sealed class SeatbeltLogo : Control
{
    public SeatbeltLogo()
    {
        DoubleBuffered = true;
        BackColor = Theme.Primary;
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        AccessibleName = "安全带耐久试验系统标识";
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (Width < 8 || Height < 8) return;

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

        var scale = Math.Min(Width, Height) / 46f;
        var offsetX = (Width - 46f * scale) / 2f;
        var offsetY = (Height - 46f * scale) / 2f;
        var state = e.Graphics.Save();
        e.Graphics.TranslateTransform(offsetX, offsetY);
        e.Graphics.ScaleTransform(scale, scale);

        using var white = new Pen(Color.White, 3.2f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };
        using var softWhite = new SolidBrush(Color.FromArgb(238, 247, 255));
        using var buckle = new SolidBrush(Color.FromArgb(74, 204, 235));

        // Occupant: head and a single flowing back/seat line.
        e.Graphics.FillEllipse(softWhite, 12.5f, 8f, 8f, 8f);
        using (var body = new GraphicsPath())
        {
            body.AddBezier(15.5f, 19f, 13f, 25f, 15f, 33f, 21.5f, 36f);
            body.AddLine(21.5f, 36f, 32f, 36f);
            e.Graphics.DrawPath(white, body);
        }

        // Shoulder belt and lower anchor make the symbol readable at 46 px.
        e.Graphics.DrawLine(white, 26.5f, 11f, 15.5f, 34f);
        e.Graphics.DrawLine(white, 17.5f, 24.5f, 29.5f, 33f);

        // Cyan buckle provides a small technical accent without adding text.
        e.Graphics.FillRoundedRectangle(buckle, new RectangleF(27f, 30.5f, 7.5f, 6f), 1.4f);

        e.Graphics.Restore(state);
    }
}

internal static class GraphicsSeatbeltExtensions
{
    public static void FillRoundedRectangle(this Graphics graphics, Brush brush, RectangleF rect, float radius)
    {
        using var path = new GraphicsPath();
        var diameter = radius * 2f;
        path.AddArc(rect.Left, rect.Top, diameter, diameter, 180, 90);
        path.AddArc(rect.Right - diameter, rect.Top, diameter, diameter, 270, 90);
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rect.Left, rect.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        graphics.FillPath(brush, path);
    }
}
