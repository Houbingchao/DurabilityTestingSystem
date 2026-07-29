using System.Drawing.Drawing2D;
using DurabilityTestingSystem.Models;

namespace DurabilityTestingSystem.UI.Controls;

public sealed class TrendChart : Control
{
    private readonly List<LiveSample> _samples = [];
    private const int MaxSamples = 240;

    public double ForceMax { get; set; } = 700;
    public IReadOnlyList<LiveSample> Samples => _samples;

    public TrendChart()
    {
        DoubleBuffered = true;
        BackColor = Color.White;
        Font = Theme.Font(8);
        SetStyle(ControlStyles.ResizeRedraw, true);
    }

    public void AddSample(LiveSample sample)
    {
        _samples.Add(sample);
        if (_samples.Count > MaxSamples) _samples.RemoveAt(0);
        Invalidate();
    }

    public void Clear()
    {
        _samples.Clear();
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        var plot = new Rectangle(62, 48, Math.Max(10, Width - 88), Math.Max(10, Height - 84));
        DrawTitleAndLegend(e.Graphics);
        DrawGrid(e.Graphics, plot);
        if (_samples.Count < 2)
        {
            using var brush = new SolidBrush(Theme.Muted);
            e.Graphics.DrawString("启动试验后显示实时采集曲线", Theme.Font(10), brush, plot,
                new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center });
            return;
        }

        DrawSeries(e.Graphics, plot, s => s.Force, ForceMax, Theme.Primary, 2.2f);
        DrawSeries(e.Graphics, plot, s => s.Current, 8, Theme.Orange, 1.8f);
        DrawSeries(e.Graphics, plot, s => s.Voltage, 60, Theme.Green, 1.8f);
        DrawLatestMarker(e.Graphics, plot);
    }

    private void DrawTitleAndLegend(Graphics g)
    {
        using var titleBrush = new SolidBrush(Theme.Text);
        g.DrawString("实时趋势曲线", Theme.Font(11, FontStyle.Bold), titleBrush, 18, 15);

        var items = new[]
        {
            ("拉力 (N)", Theme.Primary),
            ("电流 (A)", Theme.Orange),
            ("电压 (V)", Theme.Green)
        };
        var x = Math.Max(240, Width - 300);
        foreach (var (text, color) in items)
        {
            using var pen = new Pen(color, 3);
            g.DrawLine(pen, x, 24, x + 18, 24);
            using var brush = new SolidBrush(Theme.Muted);
            g.DrawString(text, Font, brush, x + 23, 17);
            x += 94;
        }
    }

    private void DrawGrid(Graphics g, Rectangle plot)
    {
        using var gridPen = new Pen(Color.FromArgb(232, 237, 243), 1) { DashStyle = DashStyle.Dash };
        using var axisPen = new Pen(Color.FromArgb(189, 201, 215));
        using var textBrush = new SolidBrush(Theme.Muted);

        for (var i = 0; i <= 5; i++)
        {
            var y = plot.Top + plot.Height * i / 5;
            g.DrawLine(gridPen, plot.Left, y, plot.Right, y);
            var forceValue = ForceMax * (5 - i) / 5;
            g.DrawString(forceValue.ToString("0"), Font, textBrush,
                new Rectangle(5, y - 9, 48, 18),
                new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center });
        }
        for (var i = 0; i <= 6; i++)
        {
            var x = plot.Left + plot.Width * i / 6;
            g.DrawLine(gridPen, x, plot.Top, x, plot.Bottom);
            var windowSeconds = _samples.Count > 1
                ? Math.Max(1, (_samples[^1].Time - _samples[0].Time).TotalSeconds)
                : 60;
            var seconds = (6 - i) * windowSeconds / 6;
            var timeLabel = i == 6
                ? "现在"
                : windowSeconds >= 10 ? $"-{seconds:0}s" : $"-{seconds:0.0}s";
            g.DrawString(timeLabel, Font, textBrush,
                new Rectangle(x - 25, plot.Bottom + 7, 50, 18),
                new StringFormat { Alignment = StringAlignment.Center });
        }
        g.DrawLine(axisPen, plot.Left, plot.Top, plot.Left, plot.Bottom);
        g.DrawLine(axisPen, plot.Left, plot.Bottom, plot.Right, plot.Bottom);
    }

    private void DrawSeries(Graphics g, Rectangle plot, Func<LiveSample, double> selector,
        double max, Color color, float width)
    {
        var points = new PointF[_samples.Count];
        for (var i = 0; i < _samples.Count; i++)
        {
            var x = plot.Left + (float)i / Math.Max(1, _samples.Count - 1) * plot.Width;
            var normalized = Math.Clamp(selector(_samples[i]) / max, 0, 1);
            var y = plot.Bottom - (float)normalized * plot.Height;
            points[i] = new PointF(x, y);
        }
        using var pen = new Pen(color, width) { LineJoin = LineJoin.Round };
        g.DrawLines(pen, points);
    }

    private void DrawLatestMarker(Graphics g, Rectangle plot)
    {
        var last = _samples[^1];
        var x = plot.Right;
        var y = plot.Bottom - (float)Math.Clamp(last.Force / ForceMax, 0, 1) * plot.Height;
        using var outer = new SolidBrush(Color.FromArgb(70, Theme.Primary));
        using var inner = new SolidBrush(Theme.Primary);
        g.FillEllipse(outer, x - 7, y - 7, 14, 14);
        g.FillEllipse(inner, x - 3, y - 3, 6, 6);
    }
}
