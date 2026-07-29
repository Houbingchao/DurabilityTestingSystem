using DurabilityTestingSystem.Data;
using DurabilityTestingSystem.Infrastructure;
using DurabilityTestingSystem.Models;
using DurabilityTestingSystem.UI;

namespace DurabilityTestingSystem;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);

        var captureArgFromCommandLine = args.FirstOrDefault(x =>
            x.StartsWith("--capture=", StringComparison.OrdinalIgnoreCase));

        try
        {
            var profile = RuntimeProfileLoader.Load();
            var modeArg = args.FirstOrDefault(x => x.StartsWith("--mode=", StringComparison.OrdinalIgnoreCase));
            if (modeArg is not null && Enum.TryParse<RuntimeMode>(modeArg["--mode=".Length..], true, out var overrideMode))
            {
                profile.Mode = overrideMode;
                profile.ProfileName = $"命令行临时配置（{overrideMode}）";
            }

            var databaseFileName = profile.Mode == RuntimeMode.Demo ? "durability-demo.db" : "durability.db";
            using var database = new AppDatabase(databaseFileName);
            database.Initialize(profile.Mode == RuntimeMode.Demo);
            ITestEngine engine = profile.Mode == RuntimeMode.Demo
                ? new DemoTestEngine()
                : new HardwareTestEngine(RuntimeProfileLoader.LoadHardwarePlatform(profile));
            var mainForm = new MainForm(database, engine, profile);
            var captureArg = args.FirstOrDefault(x => x.StartsWith("--capture=", StringComparison.OrdinalIgnoreCase));
            if (captureArg is not null)
            {
                var outputPath = captureArg["--capture=".Length..].Trim('"');
                var pageArg = args.FirstOrDefault(x => x.StartsWith("--page=", StringComparison.OrdinalIgnoreCase));
                var page = pageArg?["--page=".Length..] ?? "control";
                var widthArg = args.FirstOrDefault(x => x.StartsWith("--width=", StringComparison.OrdinalIgnoreCase));
                var heightArg = args.FirstOrDefault(x => x.StartsWith("--height=", StringComparison.OrdinalIgnoreCase));
                var captureWidth = int.TryParse(widthArg?["--width=".Length..], out var parsedWidth) ? parsedWidth : 1600;
                var captureHeight = int.TryParse(heightArg?["--height=".Length..], out var parsedHeight) ? parsedHeight : 900;
                mainForm.WindowState = FormWindowState.Normal;
                mainForm.Size = new Size(Math.Max(1280, captureWidth), Math.Max(760, captureHeight));
                mainForm.Show();
                Application.DoEvents();
                mainForm.ShowPageForCapture(page);
                var renderUntil = DateTime.UtcNow.AddMilliseconds(page == "control" ? 1800 : 450);
                while (DateTime.UtcNow < renderUntil)
                {
                    Application.DoEvents();
                    Thread.Sleep(40);
                }
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
                using var bitmap = new Bitmap(mainForm.Width, mainForm.Height);
                mainForm.DrawToBitmap(bitmap, new Rectangle(Point.Empty, mainForm.Size));
                bitmap.Save(outputPath, System.Drawing.Imaging.ImageFormat.Png);
                mainForm.Close();
                return;
            }
            Application.Run(mainForm);
        }
        catch (Exception ex)
        {
            if (captureArgFromCommandLine is not null)
            {
                var capturePath = captureArgFromCommandLine["--capture=".Length..].Trim('"');
                var errorPath = Path.GetFullPath(capturePath + ".error.txt");
                Directory.CreateDirectory(Path.GetDirectoryName(errorPath)!);
                File.WriteAllText(errorPath, ex.ToString());
                return;
            }
            MessageBox.Show(
                $"系统启动失败：\n\n{ex.Message}\n\n请联系设备管理员。",
                "安全带耐久试验系统",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }
}
