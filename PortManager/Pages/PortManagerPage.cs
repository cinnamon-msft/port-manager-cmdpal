using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using System.Diagnostics;
using System.Management;

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

            // Batch-query all command lines in one WMI call
            var pids = activeConnections.Select(x => x.Pid).Distinct().ToList();
            var commandLines = GetCommandLines(pids);

            foreach (var (port, pid) in activeConnections.OrderBy(x => x.Port))
            {
                commandLines.TryGetValue(pid, out var cmdLine);
                var info = GetProcessInfo(pid, cmdLine ?? "");

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

                var item = new ListItem(stopCommand)
                {
                    Title = $":{port}  —  {info.DisplayName}",
                    Subtitle = info.Subtitle,
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
                Arguments = "-ano",
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

    private record ProcessInfo(string Name, string DisplayName, string Subtitle);
    private record CommandInfo(string ProjectName, string ToolName);

    private static Dictionary<int, string> GetCommandLines(List<int> pids)
    {
        var result = new Dictionary<int, string>();
        if (pids.Count == 0)
        {
            return result;
        }

        try
        {
            var pidFilter = string.Join(" OR ", pids.Select(p => $"ProcessId={p}"));
            using var searcher = new ManagementObjectSearcher(
                $"SELECT ProcessId, CommandLine FROM Win32_Process WHERE {pidFilter}");

            foreach (var obj in searcher.Get())
            {
                var pid = Convert.ToInt32(obj["ProcessId"]);
                var cmd = obj["CommandLine"]?.ToString() ?? "";
                result[pid] = cmd;
            }
        }
        catch
        {
            // WMI query failed — return empty
        }

        return result;
    }

    private static ProcessInfo GetProcessInfo(int pid, string commandLine)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            var name = process.ProcessName;
            var cmdInfo = !string.IsNullOrEmpty(commandLine)
                ? ExtractCommandInfo(commandLine, name)
                : new CommandInfo("", "");

            var workingDir = "";
            if (string.IsNullOrEmpty(cmdInfo.ProjectName) && !string.IsNullOrEmpty(commandLine))
            {
                workingDir = ExtractProjectDir(commandLine);
            }

            var displayName = !string.IsNullOrEmpty(cmdInfo.ProjectName)
                ? cmdInfo.ProjectName
                : BuildDisplayName(name, "", workingDir);

            var subtitleParts = new List<string> { name };
            if (!string.IsNullOrEmpty(cmdInfo.ToolName))
            {
                subtitleParts.Add(cmdInfo.ToolName);
            }

            subtitleParts.Add($"PID {pid}");
            var subtitle = string.Join(" · ", subtitleParts);

            return new ProcessInfo(name, displayName, subtitle);
        }
        catch
        {
            return new ProcessInfo("Unknown", "Unknown process", $"PID {pid}");
        }
    }

    private static string BuildDisplayName(string processName, string commandHint, string workingDir)
    {
        // If we found a specific tool/framework, use that
        if (!string.IsNullOrEmpty(commandHint))
        {
            return commandHint;
        }

        // Try to get project name from the working directory
        if (!string.IsNullOrEmpty(workingDir))
        {
            var dirName = new DirectoryInfo(workingDir).Name;

            // Skip generic directory names
            if (!string.Equals(dirName, "bin", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(dirName, "Debug", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(dirName, "Release", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(dirName, "net8.0", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(dirName, "net9.0", StringComparison.OrdinalIgnoreCase)
                && !dirName.StartsWith("net", StringComparison.OrdinalIgnoreCase))
            {
                return dirName;
            }

            // Walk up from bin/Debug/net9.0 to the project directory
            var parent = Directory.GetParent(workingDir);
            while (parent is not null)
            {
                var pName = parent.Name;
                if (!string.Equals(pName, "bin", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(pName, "Debug", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(pName, "Release", StringComparison.OrdinalIgnoreCase)
                    && !pName.StartsWith("net", StringComparison.OrdinalIgnoreCase))
                {
                    return pName;
                }

                parent = parent.Parent;
            }
        }

        return processName;
    }

    private static string ExtractProjectDir(string commandLine)
    {
        // Try to find a directory path in the command line
        var parts = commandLine.Split('"', StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (trimmed.Contains('\\') || trimmed.Contains('/'))
            {
                try
                {
                    var dir = Path.GetDirectoryName(trimmed);
                    if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                    {
                        return dir;
                    }
                }
                catch
                {
                    // Not a valid path
                }
            }
        }

        return "";
    }

    private static CommandInfo ExtractCommandInfo(string commandLine, string processName)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
        {
            return new CommandInfo("", "");
        }

        if (string.Equals(processName, "node", StringComparison.OrdinalIgnoreCase))
        {
            var knownTools = new[] { "vite", "next", "nuxt", "remix", "astro", "webpack", "esbuild", "tsx", "ts-node", "react-scripts" };
            var tool = "";
            foreach (var t in knownTools)
            {
                if (commandLine.Contains(t, StringComparison.OrdinalIgnoreCase))
                {
                    tool = t;
                    break;
                }
            }

            var projectName = TryGetNodeProjectName(commandLine);

            if (string.IsNullOrEmpty(projectName))
            {
                var parts = commandLine.Split(' ');
                var script = parts.LastOrDefault(p => p.EndsWith(".js", StringComparison.OrdinalIgnoreCase)
                                                    || p.EndsWith(".ts", StringComparison.OrdinalIgnoreCase)
                                                    || p.EndsWith(".mjs", StringComparison.OrdinalIgnoreCase));
                if (script is not null)
                {
                    projectName = Path.GetFileName(script);
                }
            }

            return new CommandInfo(projectName, tool);
        }

        if (string.Equals(processName, "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            var projectName = "";
            if (commandLine.Contains("--project", StringComparison.OrdinalIgnoreCase))
            {
                var idx = commandLine.IndexOf("--project", StringComparison.OrdinalIgnoreCase);
                var rest = commandLine[(idx + 10)..].TrimStart();
                projectName = Path.GetFileName(rest.Split(' ')[0]);
            }
            else
            {
                var dllPart = commandLine.Split(' ')
                    .LastOrDefault(p => p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase));
                if (dllPart is not null)
                {
                    projectName = Path.GetFileNameWithoutExtension(dllPart);
                }
            }

            var tool = commandLine.Contains(" run", StringComparison.OrdinalIgnoreCase) ? "dotnet run" : "";
            return new CommandInfo(projectName, tool);
        }

        if (processName.StartsWith("python", StringComparison.OrdinalIgnoreCase))
        {
            var knownFrameworks = new[] { "uvicorn", "gunicorn", "flask", "django", "fastapi", "streamlit" };
            var tool = "";
            foreach (var fw in knownFrameworks)
            {
                if (commandLine.Contains(fw, StringComparison.OrdinalIgnoreCase))
                {
                    tool = fw;
                    break;
                }
            }

            var pyFile = commandLine.Split(' ')
                .LastOrDefault(p => p.EndsWith(".py", StringComparison.OrdinalIgnoreCase));
            var projectName = pyFile is not null ? Path.GetFileName(pyFile) : "";

            return new CommandInfo(projectName, tool);
        }

        return new CommandInfo("", "");
    }

    private static string TryGetNodeProjectName(string commandLine)
    {
        try
        {
            var segments = commandLine.Split(new[] { ' ', '"' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var segment in segments)
            {
                if (!segment.Contains('\\') && !segment.Contains('/'))
                {
                    continue;
                }

                string? dir = null;
                try
                {
                    dir = File.Exists(segment) ? Path.GetDirectoryName(segment) : null;
                }
                catch
                {
                    continue;
                }

                if (string.IsNullOrEmpty(dir))
                {
                    continue;
                }

                // If the path is inside node_modules, jump directly to the project root
                var nmIndex = dir.IndexOf("node_modules", StringComparison.OrdinalIgnoreCase);
                if (nmIndex > 0)
                {
                    var projectRoot = dir[..(nmIndex - 1)];
                    var rootPkgJson = Path.Combine(projectRoot, "package.json");
                    if (File.Exists(rootPkgJson))
                    {
                        try
                        {
                            var content = File.ReadAllText(rootPkgJson);
                            var nameMatch = System.Text.RegularExpressions.Regex.Match(
                                content, @"""name""\s*:\s*""([^""]+)""");
                            if (nameMatch.Success)
                            {
                                return nameMatch.Groups[1].Value;
                            }
                        }
                        catch { }

                        return new DirectoryInfo(projectRoot).Name;
                    }

                    continue;
                }

                // Walk up looking for package.json
                var current = new DirectoryInfo(dir);
                while (current is not null)
                {
                    var packageJson = Path.Combine(current.FullName, "package.json");
                    if (File.Exists(packageJson))
                    {
                        try
                        {
                            var content = File.ReadAllText(packageJson);
                            var nameMatch = System.Text.RegularExpressions.Regex.Match(
                                content, @"""name""\s*:\s*""([^""]+)""");
                            if (nameMatch.Success)
                            {
                                return nameMatch.Groups[1].Value;
                            }
                        }
                        catch { }

                        return current.Name;
                    }

                    current = current.Parent;
                }
            }
        }
        catch
        {
            // Best effort
        }

        return "";
    }
}
