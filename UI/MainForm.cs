using DurabilityTestingSystem.Data;
using DurabilityTestingSystem.Infrastructure;
using DurabilityTestingSystem.Models;
using DurabilityTestingSystem.UI.Controls;
using DurabilityTestingSystem.UI.Pages;

namespace DurabilityTestingSystem.UI;

public sealed class MainForm : Form
{
    private readonly AppDatabase _database;
    private readonly ITestEngine _engine;
    private readonly SystemProfile _profile;
    private TestSettings _settings;
    private readonly Panel _content;
    private readonly Label _pageTitle;
    private readonly Label _clock;
    private readonly StatusPill _analogStatus;
    private readonly StatusPill _canStatus;
    private readonly Label _footerLeft;
    private readonly Label _footerRight;
    private readonly Dictionary<string, NavButton> _navButtons = [];
    private readonly Dictionary<string, Control> _pages = [];
    private readonly System.Windows.Forms.Timer _clockTimer;
    private TestControlPage? _controlPage;
    private bool _closeInProgress;
    private bool _closeApproved;

    public MainForm(AppDatabase database, ITestEngine engine, SystemProfile profile)
    {
        _database = database;
        _engine = engine;
        _profile = profile;
        _settings = database.LoadSettings();
        _engine.ApplySettings(_settings);

        Text = "安全带耐久试验系统";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1280, 760);
        Size = new Size(1680, 980);
        WindowState = FormWindowState.Maximized;
        BackColor = Theme.Window;
        Font = Theme.Font(9);
        Icon = LoadApplicationIcon();

        var sidebar = BuildSidebar();
        var header = BuildHeader(out _pageTitle, out _clock, out _analogStatus, out _canStatus);
        var footer = BuildFooter(out _footerLeft, out _footerRight);
        _content = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Window,
            Padding = new Padding(22, 18, 22, 18)
        };
        Theme.EnableDoubleBuffer(_content);

        // Keep the top/bottom chrome inside a dedicated main-area container.
        // This removes the form-level Dock ordering dependency that could make
        // the header cover the sidebar after a clock/status layout refresh.
        var mainArea = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Window
        };
        mainArea.Controls.Add(_content);
        mainArea.Controls.Add(footer);
        mainArea.Controls.Add(header);

        Controls.Add(mainArea);
        Controls.Add(sidebar);

        CreatePages();
        Navigate("control");
        UpdateHealth(_engine.Health);
        _engine.HealthChanged += (_, health) => UpdateHealth(health);

        _clockTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _clockTimer.Tick += (_, _) => _clock.Text = DateTime.Now.ToString("yyyy-MM-dd  HH:mm:ss");
        _clockTimer.Start();
        _clock.Text = DateTime.Now.ToString("yyyy-MM-dd  HH:mm:ss");

        FormClosing += MainFormOnFormClosing;
        Shown += async (_, _) =>
        {
            if (!_profile.AutoConnectOnStartup) return;
            var result = await _engine.ConnectAndSelfCheckAsync();
            _database.AddLog(result.Success ? "信息" : "报警", "系统启动", result.Message);
        };
    }

    private Panel BuildSidebar()
    {
        var sidebar = new Panel { Dock = DockStyle.Left, Width = 226, BackColor = Theme.Sidebar };
        var brand = new Panel { Dock = DockStyle.Top, Height = 96, BackColor = Theme.Sidebar };
        var logo = new SeatbeltLogo
        {
            Location = new Point(20, 21),
            Size = new Size(46, 46)
        };
        var name = UiFactory.Label("安全带耐久试验", 11, Color.White, FontStyle.Bold);
        name.Location = new Point(78, 22);
        var version = UiFactory.Label("TEST CONTROL  ·  V1.3", 7.5f, Theme.SidebarMuted);
        version.Location = new Point(78, 49);
        brand.Controls.AddRange([logo, name, version]);

        var navContainer = new Panel { Dock = DockStyle.Top, Height = 480, Padding = new Padding(0, 12, 0, 0) };
        var navItems = new[]
        {
            ("about", "    ⓘ    关于我们"),
            ("diagnostics", "    ◉    设备诊断"),
            ("logs", "    ▤    系统日志"),
            ("history", "    ◷    历史数据"),
            ("plans", "    ☷    试验方案"),
            ("settings", "    ⚙    参数设置"),
            ("control", "    ▶    试验控制")
        };
        foreach (var (key, text) in navItems)
        {
            var button = new NavButton(text);
            button.Click += (_, _) => Navigate(key);
            _navButtons[key] = button;
            navContainer.Controls.Add(button);
        }

        var demoCard = new Panel
        {
            Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
            BackColor = Color.FromArgb(23, 46, 70),
            Location = new Point(16, 660),
            Size = new Size(194, 94)
        };
        var demoTitle = UiFactory.Label(
            _profile.Mode == RuntimeMode.Demo ? "●  DEMO 演示模式" : "●  PRODUCTION 正式模式",
            8.5f,
            _profile.Mode == RuntimeMode.Demo ? Color.FromArgb(248, 178, 74) : Color.FromArgb(104, 214, 177),
            FontStyle.Bold);
        demoTitle.Location = new Point(13, 13);
        var demoText = UiFactory.Label(
            _profile.Mode == RuntimeMode.Demo
                ? "当前数据由内置模拟器生成\n不访问真实硬件"
                : "真实硬件模式\n未通过自检将禁止启动",
            7.5f, Theme.SidebarMuted);
        demoText.Location = new Point(13, 40);
        demoCard.Controls.AddRange([demoTitle, demoText]);

        sidebar.Controls.Add(demoCard);
        sidebar.Controls.Add(navContainer);
        sidebar.Controls.Add(brand);
        sidebar.Resize += (_, _) => demoCard.Top = sidebar.ClientSize.Height - demoCard.Height - 38;
        return sidebar;
    }

    private Panel BuildHeader(out Label pageTitle, out Label clock, out StatusPill analog, out StatusPill can)
    {
        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = 72,
            BackColor = Theme.Header,
            Padding = new Padding(22, 0, 20, 0)
        };
        header.Paint += (_, e) =>
        {
            using var pen = new Pen(Theme.Border);
            e.Graphics.DrawLine(pen, 0, header.Height - 1, header.Width, header.Height - 1);
        };
        pageTitle = UiFactory.Label("试验控制", 14, Theme.Text, FontStyle.Bold);
        pageTitle.Location = new Point(23, 13);
        var breadcrumb = UiFactory.Label("安全带耐久试验系统  /  工控机上位机", 7.5f, Theme.Muted);
        breadcrumb.Location = new Point(24, 43);

        var right = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            Width = 650,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(0, 19, 0, 0),
            BackColor = Color.Transparent
        };
        clock = UiFactory.Label("", 8.5f, Theme.Muted);
        clock.Size = new Size(165, 31);
        clock.TextAlign = ContentAlignment.MiddleCenter;
        clock.Margin = new Padding(0, 0, 14, 0);
        analog = new StatusPill { Caption = "采集未知", StatusColor = Theme.Muted, Margin = new Padding(4, 0, 4, 0), Width = 96 };
        can = new StatusPill { Caption = "CAN 未知", StatusColor = Theme.Muted, Margin = new Padding(4, 0, 16, 0), Width = 104 };
        var user = UiFactory.SecondaryButton("●  本机操作员", 112);
        user.Height = 32;
        user.Margin = new Padding(0);
        user.FlatAppearance.BorderSize = 1;
        user.FlatAppearance.BorderColor = Theme.Border;
        right.Controls.AddRange([clock, analog, can, user]);

        header.Controls.Add(right);
        header.Controls.Add(pageTitle);
        header.Controls.Add(breadcrumb);
        return header;
    }

    private Panel BuildFooter(out Label left, out Label right)
    {
        var footer = new Panel { Dock = DockStyle.Bottom, Height = 30, BackColor = Color.White };
        footer.Paint += (_, e) =>
        {
            using var pen = new Pen(Theme.Border);
            e.Graphics.DrawLine(pen, 0, 0, footer.Width, 0);
        };
        left = UiFactory.Label("  系统初始化中", 7.5f, Theme.Muted, dock: DockStyle.Fill);
        right = UiFactory.Label(
            _profile.Mode == RuntimeMode.Demo ? "工业控制软件 · 演示模式    " : "工业控制软件 · 正式运行模式    ",
            7.5f, Theme.Muted, dock: DockStyle.Right);
        right.Width = 240;
        right.TextAlign = ContentAlignment.MiddleRight;
        footer.Controls.Add(left);
        footer.Controls.Add(right);
        return footer;
    }

    private void CreatePages()
    {
        var control = new TestControlPage(_database, _engine, () => _settings);
        _controlPage = control;
        var settings = new SettingsPage(_database, _settings, _engine);
        settings.SettingsSaved += (_, updated) =>
        {
            _settings = updated;
            _engine.ApplySettings(updated);
            control.RefreshSettings();
        };
        var plans = new PlansPage(_database, () => _settings);
        plans.PlansChanged += (_, _) => control.RefreshPlans();
        plans.PlanApplied += (_, args) =>
        {
            args.Result = control.ApplyPlan(args.Plan);
            if (args.Result.Success) Navigate("control");
        };
        _pages["control"] = control;
        _pages["settings"] = settings;
        _pages["plans"] = plans;
        _pages["history"] = new HistoryPage(_database);
        _pages["logs"] = new LogsPage(_database);
        _pages["diagnostics"] = new DiagnosticsPage(_database, _engine, _profile);
        _pages["about"] = new AboutPage(_profile);
        foreach (var page in _pages.Values)
        {
            page.Dock = DockStyle.Fill;
            page.Visible = false;
            _content.Controls.Add(page);
        }
    }

    private void Navigate(string key)
    {
        foreach (var (itemKey, page) in _pages) page.Visible = itemKey == key;
        foreach (var (itemKey, button) in _navButtons) button.Active = itemKey == key;
        _pages[key].BringToFront();
        ClearComboSelections(_pages[key]);
        _pageTitle.Text = key switch
        {
            "control" => "试验控制",
            "settings" => "参数设置",
            "plans" => "试验方案",
            "history" => "历史数据",
            "logs" => "系统日志",
            "diagnostics" => "设备诊断",
            "about" => "关于我们",
            _ => "安全带耐久试验系统"
        };
        if (_pages[key] is IRefreshablePage refreshable) refreshable.RefreshData();
        if (_pages[key].IsHandleCreated)
            _pages[key].BeginInvoke(new Action(() => ClearComboSelections(_pages[key])));
    }

    internal void ShowPageForCapture(string key)
    {
        var pageKey = key == "settings-stations" ? "settings" : key;
        if (!_pages.ContainsKey(pageKey)) return;
        Navigate(pageKey);
        if (pageKey == "control" && _pages[pageKey] is TestControlPage controlPage)
            controlPage.StartDemoForCapture();
        if (key == "settings-stations" && _pages[pageKey] is SettingsPage settingsPage)
            settingsPage.ShowStationGridForCapture();
    }

    private static Icon LoadApplicationIcon()
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "ADR.ico");
        if (File.Exists(iconPath)) return new Icon(iconPath);
        return Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
    }

    private static void ClearComboSelections(Control root)
    {
        foreach (Control child in root.Controls)
        {
            if (child is IndustrialComboBox combo) combo.ClearTextSelection();
            if (child.HasChildren) ClearComboSelections(child);
        }
    }

    private void UpdateHealth(SystemHealthSnapshot health)
    {
        var can = health.Find("can");
        var analog = health.Find("analog");
        SetPill(_canStatus, "CAN", can);
        SetPill(_analogStatus, "采集", analog);
        _footerLeft.Text = $"  {(health.CanStartTest ? "系统就绪" : "系统未就绪")}    |    " +
                           $"模式: {health.Mode}    |    CAN: {ShortState(can)}    |    " +
                           $"模拟量: {ShortState(analog)}    |    数据库: 已连接";
    }

    private static void SetPill(StatusPill pill, string name, DeviceStatus? status)
    {
        pill.Caption = $"{name} {ShortState(status)}";
        pill.StatusColor = status?.State switch
        {
            DeviceConnectionState.Online => Theme.Green,
            DeviceConnectionState.Warning or DeviceConnectionState.Connecting => Theme.Orange,
            DeviceConnectionState.Fault => Theme.Red,
            _ => Theme.Muted
        };
    }

    private static string ShortState(DeviceStatus? status) => status?.State switch
    {
        DeviceConnectionState.Online => "在线",
        DeviceConnectionState.Warning => "警告",
        DeviceConnectionState.Fault => "故障",
        DeviceConnectionState.Connecting => "连接中",
        DeviceConnectionState.Disconnected => "离线",
        _ => "未配置"
    };

    private async void MainFormOnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_closeApproved) return;
        if (_closeInProgress)
        {
            e.Cancel = true;
            return;
        }

        if (_controlPage?.RequiresFinalization != true)
        {
            _closeApproved = true;
            DisposeRuntimeResources();
            return;
        }

        var answer = MessageBox.Show(
            "当前试验尚未安全终结。关闭软件前将对全部工位执行停机，并保存终结记录。是否继续？",
            "确认安全停机并关闭",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);
        if (answer != DialogResult.Yes)
        {
            e.Cancel = true;
            return;
        }

        e.Cancel = true;
        _closeInProgress = true;
        Enabled = false;
        var finalized = _controlPage is not null && await _controlPage.FinalizeBeforeCloseAsync();
        if (!finalized)
        {
            Enabled = true;
            _closeInProgress = false;
            return;
        }

        _closeApproved = true;
        DisposeRuntimeResources();
        BeginInvoke(new Action(Close));
    }

    private void DisposeRuntimeResources()
    {
        _clockTimer.Dispose();
        _engine.Dispose();
    }
}

public interface IRefreshablePage
{
    void RefreshData();
}
