using System.Drawing.Drawing2D;

namespace DurabilityTestingSystem.UI.Controls;

public sealed class StatusPill : Control
{
    private string _caption = "正常";
    private Color _statusColor = Theme.Green;

    public string Caption
    {
        get => _caption;
        set { _caption = value; Invalidate(); }
    }

    public Color StatusColor
    {
        get => _statusColor;
        set { _statusColor = value; Invalidate(); }
    }

    public StatusPill()
    {
        Size = new Size(88, 30);
        Font = Theme.Font(9, FontStyle.Bold);
        DoubleBuffered = true;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = Theme.RoundedRect(rect, Height / 2 - 1);
        using var bg = new SolidBrush(Color.FromArgb(24, StatusColor));
        using var fg = new SolidBrush(StatusColor);
        e.Graphics.FillPath(bg, path);
        e.Graphics.FillEllipse(fg, 12, Height / 2 - 4, 8, 8);
        e.Graphics.DrawString(Caption, Font, fg, new Rectangle(26, 0, Width - 30, Height),
            new StringFormat
            {
                Alignment = StringAlignment.Near,
                LineAlignment = StringAlignment.Center,
                FormatFlags = StringFormatFlags.NoWrap
            });
    }
}
