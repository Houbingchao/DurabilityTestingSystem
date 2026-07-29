using System.Drawing.Drawing2D;

namespace DurabilityTestingSystem.UI.Controls;

public sealed class CycleProgress : Control
{
    private int _value;
    private int _maximum = 50000;

    public int Value { get => _value; set { _value = Math.Max(0, value); Invalidate(); } }
    public int Maximum { get => _maximum; set { _maximum = Math.Max(1, value); Invalidate(); } }

    public CycleProgress()
    {
        DoubleBuffered = true;
        Size = new Size(148, 166);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        const int ringSize = 112;
        var rect = new Rectangle((Width - ringSize) / 2, 4, ringSize, ringSize);
        using var bgPen = new Pen(Color.FromArgb(231, 237, 244), 12) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        using var fgPen = new Pen(Theme.Primary, 12) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        e.Graphics.DrawArc(bgPen, rect, -90, 360);
        var progress = Math.Clamp((double)Value / Maximum, 0, 1);
        if (progress > 0) e.Graphics.DrawArc(fgPen, rect, -90, (float)(360 * progress));

        using var valueBrush = new SolidBrush(Theme.Text);
        using var mutedBrush = new SolidBrush(Theme.Muted);
        e.Graphics.DrawString(Value.ToString("N0"), Theme.Font(18, FontStyle.Bold), valueBrush,
            new Rectangle(0, 48, Width, 36),
            new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center });
        e.Graphics.DrawString($"目标 {Maximum:N0} 次", Theme.Font(8), mutedBrush,
            new Rectangle(0, 140, Width, 20),
            new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center });
    }
}
