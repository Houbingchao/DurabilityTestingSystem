namespace DurabilityTestingSystem.UI.Controls;

public sealed class NavButton : Button
{
    private bool _active;

    public bool Active
    {
        get => _active;
        set
        {
            _active = value;
            BackColor = value ? Color.FromArgb(29, 60, 91) : Theme.Sidebar;
            ForeColor = value ? Color.White : Theme.SidebarMuted;
            Invalidate();
        }
    }

    public NavButton(string text)
    {
        Text = text;
        Height = 52;
        Dock = DockStyle.Top;
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        FlatAppearance.MouseOverBackColor = Color.FromArgb(25, 51, 79);
        BackColor = Theme.Sidebar;
        ForeColor = Theme.SidebarMuted;
        Font = Theme.Font(10, FontStyle.Regular);
        TextAlign = ContentAlignment.MiddleLeft;
        Padding = new Padding(25, 0, 0, 0);
        Cursor = Cursors.Hand;
    }

    protected override void OnPaint(PaintEventArgs pevent)
    {
        base.OnPaint(pevent);
        if (!Active) return;
        using var brush = new SolidBrush(Theme.Cyan);
        pevent.Graphics.FillRectangle(brush, 0, 8, 4, Height - 16);
    }
}
