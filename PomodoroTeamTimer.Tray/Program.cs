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
        Process server;
        try
        {
            server = StartServer();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"The local server could not be started.\n\n{ex.Message}", "Pomodoro Team Timer");
            return;
        }

        using (server)
        {
        using var icon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "Pomodoro Team Timer",
            Visible = true,
            ContextMenuStrip = BuildMenu()
        };
        icon.DoubleClick += (_, _) => Open(LocalUrl);
        icon.ShowBalloonTip(3000, "Pomodoro Team Timer is running", $"Team URL: http://{GetLanAddress()}:5080", ToolTipIcon.Info);
        _ = OpenWhenReadyAsync(server);
        Application.Run();
        if (!server.HasExited) server.Kill(true);
        }
    }

    private static ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open application", null, (_, _) => Open(LocalUrl));
        menu.Items.Add($"Copy team URL: http://{GetLanAddress()}:5080", null, (_, _) => Clipboard.SetText($"http://{GetLanAddress()}:5080"));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => Application.Exit());
        return menu;
    }

    private static Process StartServer()
    {
        var packagedServer = Path.Combine(AppContext.BaseDirectory, "Server", "PomodoroTeamTimer.Server.exe");
        if (File.Exists(packagedServer))
            return Launch(packagedServer, "", Path.GetDirectoryName(packagedServer)!);

        var root = FindSolutionRoot() ?? throw new DirectoryNotFoundException("PomodoroTeamTimer.Server was not found.");
        var project = Path.Combine(root, "PomodoroTeamTimer.Server", "PomodoroTeamTimer.Server.csproj");
        return Launch("dotnet", $"run --project \"{project}\" --no-launch-profile", root);
    }

    private static Process Launch(string fileName, string arguments, string workingDirectory) =>
        Process.Start(new ProcessStartInfo(fileName, arguments)
        {
            WorkingDirectory = workingDirectory, UseShellExecute = false, CreateNoWindow = true
        }) ?? throw new InvalidOperationException("The server process did not start.");

    private static async Task OpenWhenReadyAsync(Process server)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromMilliseconds(500) };
        for (var attempt = 0; attempt < 30 && !server.HasExited; attempt++)
        {
            try
            {
                using var response = await client.GetAsync(LocalUrl);
                if (response.IsSuccessStatusCode) { Open(LocalUrl); return; }
            }
            catch { }
            await Task.Delay(300);
        }
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
