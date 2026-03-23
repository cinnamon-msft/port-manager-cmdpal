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
        // Hyper-V / virtualization
        "vmms", "vmcompute", "vmwp", "vmconnect",
        // Desktop apps that listen on ports
        "Discord", "Spotify", "Slack", "Steam", "EpicGamesLauncher",
        "chrome", "firefox", "brave",
        // Surface / hardware / notifications
        "SurfaceBroker", "SurfaceService", "SCNotification",
        "Intel", "igfxEM", "igfxCUIService",
        // Other common non-dev listeners
        "NVDisplay.Container", "NVIDIA Web Helper",
        "CmdPalGitHubExtension", "Microsoft.CmdPal.Ext.PowerToys",
        "JPSoftworks.RecentFilesExtension",
        "PortManager",
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
                var info = GetProcessInfo(pid);

                if (SystemProcesses.Contains(info.Name))
                {
                    continue;
                }

                var isDevServer = DevServerProcesses.Contains(info.Name);
                var tags = new List<Tag>();
                if (isDevServer)
                {
                    tags.Add(new Tag("dev server"));
                }

                var stopCommand = new StopProcessCommand(pid, port, info.Name, this);

                var displayTitle = string.IsNullOrEmpty(info.Description)
                    ? $":{port}  —  {info.Name}"
                    : $":{port}  —  {info.Description}";

                var item = new ListItem(stopCommand)
                {
                    Title = displayTitle,
                    Subtitle = $"{info.Name} · PID {pid}" + (string.IsNullOrEmpty(info.CommandHint) ? "" : $" · {info.CommandHint}"),
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

    private record ProcessInfo(string Name, string Description, string CommandHint);

    private static ProcessInfo GetProcessInfo(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            var name = process.ProcessName;
            var description = "";
            var commandHint = "";

            try
            {
                var mainModule = process.MainModule;
                if (mainModule?.FileVersionInfo?.FileDescription is { Length: > 0 } desc
                    && !string.Equals(desc, name, StringComparison.OrdinalIgnoreCase))
                {
                    description = desc;
                }
            }
            catch
            {
                // Access denied for some processes
            }

            // Try to get command line for context (e.g., "dotnet run --project MyApi")
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "wmic",
                    Arguments = $"process where ProcessId={pid} get CommandLine /format:list",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };

                using var wmicProc = Process.Start(psi);
                if (wmicProc is not null)
                {
                    var output = wmicProc.StandardOutput.ReadToEnd();
                    wmicProc.WaitForExit(2000);

                    var cmdLine = output
                        .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                        .FirstOrDefault(l => l.StartsWith("CommandLine=", StringComparison.OrdinalIgnoreCase));

                    if (cmdLine is not null)
                    {
                        var fullCmd = cmdLine["CommandLine=".Length..].Trim();
                        commandHint = ExtractCommandHint(fullCmd, name);
                    }
                }
            }
            catch
            {
                // wmic may not be available
            }

            // If no description yet, try to derive one from the command hint
            if (string.IsNullOrEmpty(description) && !string.IsNullOrEmpty(commandHint))
            {
                description = $"{name} ({commandHint})";
            }

            return new ProcessInfo(name, description, commandHint);
        }
        catch
        {
            return new ProcessInfo("Unknown", "", "");
        }
    }

    private static string ExtractCommandHint(string commandLine, string processName)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
        {
            return "";
        }

        // For node: extract the script being run (e.g., "vite", "next dev", "server.js")
        if (string.Equals(processName, "node", StringComparison.OrdinalIgnoreCase))
        {
            // Look for known tool names in the path
            var knownTools = new[] { "vite", "next", "nuxt", "remix", "astro", "webpack", "esbuild", "tsx", "ts-node" };
            foreach (var tool in knownTools)
            {
                if (commandLine.Contains(tool, StringComparison.OrdinalIgnoreCase))
                {
                    return tool;
                }
            }

            // Fall back to the last .js/.ts file in the command
            var parts = commandLine.Split(' ');
            var script = parts.LastOrDefault(p => p.EndsWith(".js", StringComparison.OrdinalIgnoreCase)
                                               || p.EndsWith(".ts", StringComparison.OrdinalIgnoreCase)
                                               || p.EndsWith(".mjs", StringComparison.OrdinalIgnoreCase));
            if (script is not null)
            {
                return Path.GetFileName(script);
            }
        }

        // For dotnet: extract the project/dll being run
        if (string.Equals(processName, "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            if (commandLine.Contains("--project", StringComparison.OrdinalIgnoreCase))
            {
                var idx = commandLine.IndexOf("--project", StringComparison.OrdinalIgnoreCase);
                var rest = commandLine[(idx + 10)..].TrimStart();
                var project = rest.Split(' ')[0];
                return Path.GetFileName(project);
            }

            var dllPart = commandLine.Split(' ')
                .LastOrDefault(p => p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase));
            if (dllPart is not null)
            {
                return Path.GetFileNameWithoutExtension(dllPart);
            }

            // "dotnet run" in a project directory
            if (commandLine.Contains(" run", StringComparison.OrdinalIgnoreCase))
            {
                return "dotnet run";
            }
        }

        // For python: extract the script or module
        if (processName.StartsWith("python", StringComparison.OrdinalIgnoreCase))
        {
            var knownFrameworks = new[] { "uvicorn", "gunicorn", "flask", "django", "fastapi", "streamlit" };
            foreach (var fw in knownFrameworks)
            {
                if (commandLine.Contains(fw, StringComparison.OrdinalIgnoreCase))
                {
                    return fw;
                }
            }

            var pyFile = commandLine.Split(' ')
                .LastOrDefault(p => p.EndsWith(".py", StringComparison.OrdinalIgnoreCase));
            if (pyFile is not null)
            {
                return Path.GetFileName(pyFile);
            }
        }

        return "";
    }
}
