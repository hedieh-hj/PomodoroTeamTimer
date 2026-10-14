using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace PomodoroTeamTimer.Tray;

internal static class Program
{
    private const string LocalUrl = "http://localhost:5080";

    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        var root = FindSolutionRoot();
        if (root is null)
        {
            MessageBox.Show("پوشهٔ PomodoroTeamTimer.Server پیدا نشد.", "تمرکز تیمی");
            return;
        }

        using var server = StartServer(root);
        using var icon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "Pomodoro Team Timer",
            Visible = true,
            ContextMenuStrip = BuildMenu()
        };
        icon.DoubleClick += (_, _) => Open(LocalUrl);
        icon.ShowBalloonTip(3000, "تمرکز تیمی اجرا شد", $"آدرس شبکه: http://{GetLanAddress()}:5080", ToolTipIcon.Info);
        Application.Run();
        if (!server.HasExited) server.Kill(true);
    }

    private static ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("بازکردن برنامه", null, (_, _) => Open(LocalUrl));
        menu.Items.Add($"آدرس شبکه: http://{GetLanAddress()}:5080", null, (_, _) => Clipboard.SetText($"http://{GetLanAddress()}:5080"));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("خروج", null, (_, _) => Application.Exit());
        return menu;
    }

    private static Process StartServer(string root)
    {
        var project = Path.Combine(root, "PomodoroTeamTimer.Server", "PomodoroTeamTimer.Server.csproj");
        var process = Process.Start(new ProcessStartInfo("dotnet", $"run --project \"{project}\" --no-launch-profile")
        {
            WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        }) ?? throw new InvalidOperationException("سرور اجرا نشد.");
        Task.Delay(900).ContinueWith(_ => Open(LocalUrl));
        return process;
    }

    private static void Open(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    private static string? FindSolutionRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "PomodoroTeamTimer.Server"))) return dir.FullName;
            dir = dir.Parent;
        }
        return Directory.Exists(Path.Combine(Environment.CurrentDirectory, "PomodoroTeamTimer.Server")) ? Environment.CurrentDirectory : null;
    }
    private static string GetLanAddress() => Dns.GetHostEntry(Dns.GetHostName()).AddressList
        .FirstOrDefault(x => x.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(x))?.ToString() ?? "localhost";
}
