using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using System.Diagnostics;

namespace PortManager;

public sealed partial class PortManagerPage : ListPage
{
    // Well-known system/OS processes that listen on ports but aren't user dev servers
    private static readonly HashSet<string> SystemProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "System", "svchost", "lsass", "services", "wininit", "csrss", "smss",
        "spoolsv", "SearchIndexer", "SecurityHealthService", "MsMpEng",
        "WmiPrvSE", "dasHost", "LsaIso", "fontdrvhost", "dwm", "sihost",
        "RuntimeBroker", "ShellExperienceHost", "StartMenuExperienceHost",
        "explorer", "TextInputHost", "ctfmon", "conhost", "dllhost",
        "WUDFHost", "audiodg", "mDNSResponder", "mqsvc", "sqlservr",
        "nginx", "httpd", "w3wp",
        // Windows networking / infrastructure
        "DNS", "Dnscache", "BFE", "mpssvc", "NlaSvc", "iphlpsvc",
        // Microsoft services
        "OneDrive", "Teams", "Outlook", "msedge", "msedgewebview2",
        "PowerToys", "PowerToys.Settings", "Microsoft.CmdPal",
        "SearchHost", "Widgets", "WidgetService", "PhoneExperienceHost",
        "WindowsTerminal", "OpenConsole",
        // SSH / remote
        "sshd", "ssh-agent",
    };

    // Process names commonly associated with dev servers
    private static readonly HashSet<string> DevServerProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "node", "dotnet", "python", "python3", "pythonw", "ruby", "java", "javaw",
        "go", "cargo", "rust-analyzer", "deno", "bun",
        "php", "beam.smp", "elixir", "erl", "mix",
        "uvicorn", "gunicorn", "flask", "django",
        "next-server", "vite", "webpack",
    };

    public PortManagerPage()
    {
        Icon = new IconInfo("\uE968");
        Title = "Port Manager";
        Name = "Open";
    }

    public override IListItem[] GetItems()
    {
        try
        {
            var items = new List<IListItem>();
            var activeConnections = GetActivePortMappings();

            foreach (var (port, pid) in activeConnections.OrderBy(x => x.Port))
            {
                var processName = GetProcessName(pid);

                if (SystemProcesses.Contains(processName))
                {
                    continue;
                }

                var isDevServer = DevServerProcesses.Contains(processName);
                var tags = new List<Tag>();
                if (isDevServer)
                {
                    tags.Add(new Tag("dev server"));
                }

                var stopCommand = new StopProcessCommand(pid, port, processName, this);

                var item = new ListItem(stopCommand)
                {
                    Title = $":{port}  —  {processName}",
                    Subtitle = $"PID {pid}",
                    Icon = new IconInfo(isDevServer ? "\uE774" : "\uEA3A"),
                    Tags = [.. tags],
                };

                items.Add(item);
            }

            if (items.Count == 0)
            {
                items.Add(new ListItem(new NoOpCommand())
                {
                    Title = "No dev servers running",
                    Subtitle = "Start a server and it will appear here",
                    Icon = new IconInfo("\uE930"),
                });
            }

            return [.. items];
        }
        catch (Exception ex)
        {
            return
            [
                new ListItem(new NoOpCommand())
                {
                    Title = "Error scanning ports",
                    Subtitle = ex.Message,
                    Icon = new IconInfo("\uE783"),
                },
            ];
        }
    }

    internal void Refresh()
    {
        RaiseItemsChanged();
    }

    private static List<(int Port, int Pid)> GetActivePortMappings()
    {
        var results = new List<(int Port, int Pid)>();

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "netstat.exe",
                Arguments = "-ano -p TCP",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            };

            using var proc = Process.Start(psi);
            if (proc is null)
            {
                return results;
            }

            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(5000);

            var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (!trimmed.StartsWith("TCP", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var parts = trimmed.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 5 || !string.Equals(parts[3], "LISTENING", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var localAddress = parts[1];
                var lastColon = localAddress.LastIndexOf(':');
                if (lastColon < 0)
                {
                    continue;
                }

                if (!int.TryParse(localAddress.AsSpan(lastColon + 1), out var port))
                {
                    continue;
                }

                if (!int.TryParse(parts[4], out var pid) || pid == 0)
                {
                    continue;
                }

                if (!results.Any(r => r.Port == port))
                {
                    results.Add((port, pid));
                }
            }
        }
        catch
        {
            // Return whatever we have so far
        }

        return results;
    }

    private static string GetProcessName(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return process.ProcessName;
        }
        catch
        {
            return "Unknown";
        }
    }
}
