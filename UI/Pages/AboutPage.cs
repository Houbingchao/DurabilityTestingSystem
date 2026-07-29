using System.Diagnostics;
using DurabilityTestingSystem.Models;
using DurabilityTestingSystem.UI.Controls;

namespace DurabilityTestingSystem.UI.Pages;

public sealed class AboutPage : UserControl
{
    private const string OrganizationName = "沈阳艾德瑞自动化有限公司";
    private const string Website = "http://www.aiderui.com.cn";

    public AboutPage(SystemProfile profile)
    {
        BackColor = Theme.Window;

        var hero = new CardPanel
        {
            Dock = DockStyle.Top,
            Height = 184,
            Padding = new Padding(24),
            BackColor = Color.FromArgb(18, 40, 64),
            BorderColor = Color.FromArgb(32, 64, 93)
        };
        var logo = new PictureBox
        {
            Location = new Point(28, 28),
            Size = new Size(116, 116),
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.Transparent,
            Image = LoadCompanyLogo()
        };
        var company = UiFactory.Label(OrganizationName, 22, Color.White, FontStyle.Bold);
        company.Location = new Point(174, 35);
        var english = UiFactory.Label("SHENYANG AIDERUI AUTOMATION CO., LTD.", 9, Color.FromArgb(127, 174, 217), FontStyle.Bold);
        english.Location = new Point(176, 79);
        var slogan = UiFactory.Label("专业 · 稳定 · 专注 · 共赢", 11, Color.FromArgb(102, 219, 181), FontStyle.Bold);
        slogan.Location = new Point(174, 112);
        var description = UiFactory.Label("工业自动化与测试测量系统解决方案", 9, Color.FromArgb(190, 207, 224));
        description.Location = new Point(175, 142);

        var heroRight = new Panel
        {
            Dock = DockStyle.Right,
            Width = 320,
            BackColor = Color.Transparent,
            Padding = new Padding(18, 27, 10, 18)
        };
        var systemLabel = UiFactory.Label("安全带耐久试验系统", 12, Color.White, FontStyle.Bold, DockStyle.Top);
        systemLabel.Height = 34;
        var systemType = UiFactory.Label("工业控制上位机软件", 8.5f, Color.FromArgb(155, 181, 206), dock: DockStyle.Top);
        systemType.Height = 30;
        var versionBadge = new StatusPill
        {
            Caption = $"软件版本 V{Application.ProductVersion}",
            StatusColor = profile.Mode == RuntimeMode.Demo ? Theme.Orange : Theme.Green,
            Size = new Size(176, 32),
            Location = new Point(18, 98)
        };
        heroRight.Controls.Add(versionBadge);
        heroRight.Controls.Add(systemType);
        heroRight.Controls.Add(systemLabel);
        hero.Controls.Add(heroRight);
        hero.Controls.AddRange([logo, company, english, slogan, description]);

        var body = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(0, 14, 0, 0),
            BackColor = Theme.Window
        };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));

        var contactCard = UiFactory.Card("公司与联系信息", "如需项目咨询、设备选型或技术支持，请通过以下方式联系我们");
        contactCard.Dock = DockStyle.Fill;
        contactCard.Margin = new Padding(0, 0, 8, 0);
        var contactRows = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 6,
            Padding = new Padding(0, 4, 0, 0)
        };
        var rows = new[]
        {
            ("公司名称", OrganizationName, "企"),
            ("公司地址", "中国辽宁省沈阳市沈北新区中电光谷信息港 D3 栋", "址"),
            ("全国服务热线", "18640188846", "电"),
            ("联系手机", "18640188846", "手"),
            ("电子邮箱", "18640188846@163.com", "邮"),
            ("公司官网", Website, "网")
        };
        foreach (var (title, value, glyph) in rows)
        {
            contactRows.RowStyles.Add(new RowStyle(SizeType.Percent, 16.666f));
            contactRows.Controls.Add(CreateInfoRow(title, value, glyph));
        }
        contactCard.Controls.Add(contactRows);

        var rightColumn = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Margin = new Padding(8, 0, 0, 0) };
        rightColumn.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
        rightColumn.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
        var serviceCard = BuildServiceCard();
        var softwareCard = BuildSoftwareCard(profile);
        rightColumn.Controls.Add(serviceCard, 0, 0);
        rightColumn.Controls.Add(softwareCard, 0, 1);
        body.Controls.Add(contactCard, 0, 0);
        body.Controls.Add(rightColumn, 1, 0);

        Controls.Add(body);
        Controls.Add(hero);
    }

    private static Panel CreateInfoRow(string title, string value, string glyph)
    {
        var row = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0, 2, 0, 2), BackColor = Color.FromArgb(247, 249, 252) };
        var icon = new Label
        {
            Text = glyph,
            Font = Theme.Font(10, FontStyle.Bold),
            ForeColor = Theme.Primary,
            BackColor = Theme.PrimarySoft,
            TextAlign = ContentAlignment.MiddleCenter,
            Location = new Point(12, 9),
            Size = new Size(36, 36)
        };
        var titleLabel = UiFactory.Label(title, 8, Theme.Muted);
        titleLabel.Location = new Point(63, 8);
        var valueLabel = UiFactory.Label(value, 10, Theme.Text, FontStyle.Bold);
        valueLabel.Location = new Point(63, 29);
        row.Controls.AddRange([icon, titleLabel, valueLabel]);
        return row;
    }

    private static CardPanel BuildServiceCard()
    {
        var card = UiFactory.Card("服务时间", "全国服务热线：18640188846");
        card.Dock = DockStyle.Fill;
        card.Margin = new Padding(0, 0, 0, 7);
        var weekday = UiFactory.Label("周一至周五", 9, Theme.Muted, FontStyle.Bold);
        weekday.Location = new Point(22, 72);
        var weekdayTime = UiFactory.Label("08:30 — 18:00", 15, Theme.Text, FontStyle.Bold);
        weekdayTime.Location = new Point(22, 98);
        var weekend = UiFactory.Label("周六、周日", 9, Theme.Muted, FontStyle.Bold);
        weekend.Location = new Point(245, 72);
        var weekendTime = UiFactory.Label("09:00 — 18:00", 15, Theme.Text, FontStyle.Bold);
        weekendTime.Location = new Point(245, 98);
        var hotline = UiFactory.Label("☎  技术与商务咨询：18640188846", 9, Theme.Primary, FontStyle.Bold);
        hotline.Location = new Point(22, 153);
        card.Controls.AddRange([weekday, weekdayTime, weekend, weekendTime, hotline]);
        return card;
    }

    private static CardPanel BuildSoftwareCard(SystemProfile profile)
    {
        var modeText = profile.Mode == RuntimeMode.Demo ? "Demo 演示模式" : "Production 正式模式";
        var card = UiFactory.Card("关于本软件", $"安全带耐久试验系统 · 工控机上位机 · {modeText}");
        card.Dock = DockStyle.Fill;
        card.Margin = new Padding(0, 7, 0, 0);
        var info = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 4, Padding = new Padding(0, 6, 0, 8) };
        info.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
        info.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65));
        var values = new[]
        {
            ("软件版本", $"V{Application.ProductVersion}  ·  {modeText}"),
            ("运行平台", ".NET 8 · Windows x64"),
            ("数据存储", "SQLite 本地数据库"),
            ("技术架构", "WinForms · CAN · 模拟量采集")
        };
        for (var i = 0; i < values.Length; i++)
        {
            var key = UiFactory.Label(values[i].Item1, 8.5f, Theme.Muted, FontStyle.Bold, DockStyle.Fill);
            var value = UiFactory.Label(values[i].Item2, 9, Theme.Text, FontStyle.Bold, DockStyle.Fill);
            info.Controls.Add(key, 0, i);
            info.Controls.Add(value, 1, i);
        }
        var websiteButton = UiFactory.Button("访问公司官网", Theme.Primary, Color.White, 138);
        websiteButton.Dock = DockStyle.Bottom;
        websiteButton.Click += (_, _) => OpenWebsite();
        card.Controls.Add(info);
        card.Controls.Add(websiteButton);
        return card;
    }

    private static Image? LoadCompanyLogo()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Assets", "ADR.ico");
            using var icon = new Icon(path, new Size(128, 128));
            return icon.ToBitmap();
        }
        catch
        {
            return null;
        }
    }

    private static void OpenWebsite()
    {
        try
        {
            Process.Start(new ProcessStartInfo(Website) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"无法打开公司官网：{ex.Message}", "关于我们", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
