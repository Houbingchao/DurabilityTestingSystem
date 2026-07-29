using System.Drawing.Drawing2D;

namespace DurabilityTestingSystem.UI.Controls;

public class CardPanel : Panel
{
    public Color BorderColor { get; set; } = Theme.Border;
    public int CornerRadius { get; set; } = 8;
    public bool ShowBorder { get; set; } = true;

    public CardPanel()
    {
        DoubleBuffered = true;
        BackColor = Theme.Card;
        Padding = new Padding(1);
        SetStyle(ControlStyles.ResizeRedraw, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        if (rect.Width <= 1 || rect.Height <= 1) return;
        using var path = Theme.RoundedRect(rect, CornerRadius);
        using var fill = new SolidBrush(BackColor);
        e.Graphics.FillPath(fill, path);
        if (ShowBorder)
        {
            using var pen = new Pen(BorderColor);
            e.Graphics.DrawPath(pen, path);
        }
        base.OnPaint(e);
    }
}
