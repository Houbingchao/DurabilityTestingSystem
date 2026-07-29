namespace DurabilityTestingSystem.UI.Controls;

public sealed class KpiCard : CardPanel
{
    private readonly Label _title;
    private readonly Label _value;
    private readonly Label _unit;
    private readonly Label _note;
    private readonly Panel _accent;

    public string Value
    {
        get => _value.Text;
        set
        {
            _value.Text = value;
            PositionUnit();
        }
    }
    public string Note { get => _note.Text; set => _note.Text = value; }

    public KpiCard(string title, string value, string unit, string note, Color color)
    {
        Height = 118;
        _accent = new Panel { BackColor = color, Dock = DockStyle.Left, Width = 5 };
        _title = new Label
        {
            Text = title,
            Font = Theme.Font(9),
            ForeColor = Theme.Muted,
            AutoSize = true,
            Location = new Point(20, 16)
        };
        _value = new Label
        {
            Text = value,
            Font = Theme.Font(22, FontStyle.Bold),
            ForeColor = Theme.Text,
            AutoSize = true,
            Location = new Point(18, 40)
        };
        _unit = new Label
        {
            Text = unit,
            Font = Theme.Font(9),
            ForeColor = Theme.Muted,
            AutoSize = true,
            Location = new Point(126, 61)
        };
        _note = new Label
        {
            Text = note,
            Font = Theme.Font(8),
            ForeColor = color,
            AutoSize = true,
            Location = new Point(20, 91)
        };
        Controls.AddRange([_note, _unit, _value, _title, _accent]);
        _value.SizeChanged += (_, _) => PositionUnit();
        Resize += (_, _) => PositionUnit();
        PositionUnit();
    }

    private void PositionUnit()
    {
        var preferredLeft = _value.Right + 9;
        var maximumLeft = Math.Max(18, ClientSize.Width - _unit.Width - 18);
        _unit.Left = Math.Min(preferredLeft, maximumLeft);
    }
}
