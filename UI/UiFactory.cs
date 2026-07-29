using System.Globalization;
using DurabilityTestingSystem.UI.Controls;

namespace DurabilityTestingSystem.UI;

internal static class UiFactory
{
    public static Label Label(string text, float size = 9, Color? color = null,
        FontStyle style = FontStyle.Regular, DockStyle dock = DockStyle.None)
        => new()
        {
            Text = text,
            Font = Theme.Font(size, style),
            ForeColor = color ?? Theme.Text,
            AutoSize = dock == DockStyle.None,
            Dock = dock,
            TextAlign = ContentAlignment.MiddleLeft,
            BackColor = Color.Transparent
        };

    public static Button Button(string text, Color? backColor = null, Color? foreColor = null,
        int width = 104, int height = 38)
    {
        var button = new Button
        {
            Text = text,
            Size = new Size(width, height),
            BackColor = backColor ?? Theme.Primary,
            ForeColor = foreColor ?? Color.White,
            Font = Theme.Font(9, FontStyle.Bold),
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand,
            Margin = new Padding(6)
        };
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = ControlPaint.Light(button.BackColor, .08f);
        return button;
    }

    public static Button SecondaryButton(string text, int width = 104) =>
        Button(text, Color.White, Theme.Text, width);

    public static TextBox TextBox(string text = "") => new()
    {
        Text = text,
        BorderStyle = BorderStyle.FixedSingle,
        Font = Theme.Font(9),
        ForeColor = Theme.Text,
        BackColor = Color.White,
        Height = 34,
        Margin = new Padding(3, 7, 3, 7)
    };

    public static NumericUpDown Numeric(decimal value, decimal min, decimal max, int decimals = 0,
        decimal increment = 1)
    {
        var control = new NumericUpDown
        {
            Minimum = min,
            Maximum = max,
            DecimalPlaces = decimals,
            Increment = increment,
            BorderStyle = BorderStyle.FixedSingle,
            Font = Theme.Font(9),
            ForeColor = Theme.Text,
            BackColor = Color.White,
            Height = 34,
            ThousandsSeparator = true,
            Margin = new Padding(3, 7, 3, 7)
        };
        control.Value = Math.Clamp(value, min, max);
        return control;
    }

    public static ComboBox Combo(IEnumerable<string> items, string selected)
    {
        var combo = new IndustrialComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDown,
            FlatStyle = FlatStyle.Standard,
            Font = Theme.Font(9),
            ForeColor = Theme.Text,
            BackColor = Color.White,
            Height = 34,
            Margin = new Padding(3, 7, 3, 7)
        };
        combo.Items.AddRange(items.Cast<object>().ToArray());
        combo.SelectedItem = selected;
        if (combo.SelectedIndex < 0 && combo.Items.Count > 0) combo.SelectedIndex = 0;
        combo.KeyPress += (_, e) => e.Handled = true;
        return combo;
    }

    public static CardPanel Card(string title, string? subtitle = null)
    {
        var card = new CardPanel { Padding = new Padding(18, 58, 18, 16) };
        var titleLabel = Label(title, 11, Theme.Text, FontStyle.Bold);
        titleLabel.Location = new Point(18, 17);
        card.Controls.Add(titleLabel);
        if (!string.IsNullOrWhiteSpace(subtitle))
        {
            var subtitleLabel = Label(subtitle, 8, Theme.Muted);
            subtitleLabel.Location = new Point(18, 38);
            card.Controls.Add(subtitleLabel);
        }
        return card;
    }

    public static DataGridView Grid()
    {
        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            BackgroundColor = Color.White,
            BorderStyle = BorderStyle.None,
            CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
            GridColor = Color.FromArgb(231, 236, 242),
            RowHeadersVisible = false,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            MultiSelect = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            ColumnHeadersHeight = 42,
            RowTemplate = { Height = 40 },
            EnableHeadersVisualStyles = false,
            Font = Theme.Font(8.5f),
            ForeColor = Theme.Text,
            DefaultCellStyle =
            {
                BackColor = Color.White,
                SelectionBackColor = Theme.PrimarySoft,
                SelectionForeColor = Theme.PrimaryDark,
                Padding = new Padding(6, 0, 6, 0)
            },
            ColumnHeadersDefaultCellStyle =
            {
                BackColor = Color.FromArgb(247, 249, 252),
                ForeColor = Theme.Muted,
                Font = Theme.Font(8.5f, FontStyle.Bold),
                Alignment = DataGridViewContentAlignment.MiddleLeft,
                Padding = new Padding(6, 0, 6, 0)
            }
        };
        Theme.EnableDoubleBuffer(grid);
        return grid;
    }

    public static Panel Field(string label, Control input, string? unit = null, string? hint = null)
    {
        var panel = new Panel
        {
            Size = new Size(320, hint is null ? 44 : 62),
            Dock = DockStyle.Top,
            Margin = new Padding(0)
        };
        var labelControl = Label(label, 8.5f, Theme.Text, FontStyle.Bold);
        labelControl.Location = new Point(0, 0);
        panel.Controls.Add(labelControl);
        input.Location = new Point(0, 18);
        input.Height = 23;
        input.Width = string.IsNullOrEmpty(unit) ? panel.Width : panel.Width - 60;
        input.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
        input.Margin = new Padding(0);
        panel.Controls.Add(input);
        if (!string.IsNullOrEmpty(unit))
        {
            var unitLabel = Label(unit, 8.5f, Theme.Muted);
            unitLabel.Location = new Point(panel.Width - 54, 22);
            unitLabel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            panel.Controls.Add(unitLabel);
        }
        if (!string.IsNullOrEmpty(hint))
        {
            var hintLabel = Label(hint, 7.5f, Theme.Muted);
            hintLabel.Location = new Point(0, 44);
            panel.Controls.Add(hintLabel);
        }
        return panel;
    }

    public static string FormatDuration(TimeSpan duration) =>
        duration.TotalHours >= 1
            ? duration.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture)
            : duration.ToString(@"mm\:ss", CultureInfo.InvariantCulture);
}
