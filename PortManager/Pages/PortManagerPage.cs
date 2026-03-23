using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using System.Diagnostics;
using System.Net.NetworkInformation;

namespace PortManager;

public sealed partial class PortManagerPage : ListPage
{
    private static readonly int[] CommonDevPorts =
    [
        3000, 3001, 4200, 4321, 5000, 5001, 5173, 5174,
        8000, 8080, 8081, 8443, 8888, 9000, 9090, 9229,
    ];

    public PortManagerPage()
    {
        Icon = new IconInfo("\uE968");
        Title = "Port Manager";
        Name = "Open";
    }

    public override IListItem[] GetItems()
    {
        var items = new List<IListItem>();

        try
        {
            var activeConnections = GetActivePortMappings();

            foreach (var (port, pid) in activeConnections.OrderBy(x => x.Port))
            {
                var processName = GetProcessName(pid);
                var isCommonPort = CommonDevPorts.Contains(port);
                var tag = isCommonPort ? new Tag("dev") : null;

                var stopCommand = new StopProcessCommand(pid, port, processName, this);

                var item = new ListItem(stopCommand)
                {
                    Title = $":{port}",
                    Subtitle = $"{processName} (PID {pid})",
                    Icon = new IconInfo("\uEA3A"),
                    Tags = tag is not null ? [tag] : [],
                };

                items.Add(item);
            }
        }
        catch (Exception ex)
        {
            Debug.Write($"PortManager error: {ex}");
            items.Add(new ListItem(new NoOpCommand())
            {
                Title = "Error scanning ports",
                Subtitle = ex.Message,
                Icon = new IconInfo("\uE783"),
            });
            return [.. items];
        }

        if (items.Count == 0)
        {
            items.Add(new ListItem(new NoOpCommand())
            {
                Title = "No active listeners found",
                Subtitle = "All common dev ports are free",
                Icon = new IconInfo("\uE930"),
            });
        }

        return [.. items];
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
            var startInfo = new ProcessStartInfo
            {
                FileName = "netstat",
                Arguments = "-ano -p TCP",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return results;
            }

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            foreach (var line in output.Split('\n'))
            {
                var trimmed = line.Trim();
                if (!trimmed.StartsWith("TCP"))
                {
                    continue;
                }

                var parts = trimmed.Split([' '], StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 5 || parts[3] != "LISTENING")
                {
                    continue;
                }

                var localAddress = parts[1];
                var lastColon = localAddress.LastIndexOf(':');
                if (lastColon < 0)
                {
                    continue;
                }

                if (!int.TryParse(localAddress[(lastColon + 1)..], out var port))
                {
                    continue;
                }

                if (!int.TryParse(parts[4], out var pid))
                {
                    continue;
                }

                if (pid == 0)
                {
                    continue;
                }

                // Deduplicate — keep first occurrence of each port
                if (!results.Any(r => r.Port == port))
                {
                    results.Add((port, pid));
                }
            }
        }
        catch
        {
            // Silently fail if netstat is unavailable
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
