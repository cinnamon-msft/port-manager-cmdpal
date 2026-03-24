using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using System.Diagnostics;

namespace PortManager;

public sealed partial class StopProcessCommand : InvokableCommand
{
    private readonly int _pid;
    private readonly int _port;
    private readonly string _processName;
    private readonly PortManagerPage _page;

    public StopProcessCommand(int pid, int port, string processName, PortManagerPage page)
    {
        _pid = pid;
        _port = port;
        _processName = processName;
        _page = page;
        Name = $"Stop {processName} on :{port}";
        Icon = new IconInfo("\uE711"); // Cancel icon
    }

    public override CommandResult Invoke()
    {
        try
        {
            // Try taskkill /T for full process tree (works better with node)
            var psi = new ProcessStartInfo
            {
                FileName = "taskkill",
                Arguments = $"/PID {_pid} /T /F",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            using var killProc = Process.Start(psi);
            killProc?.WaitForExit(5000);
        }
        catch
        {
            // Fallback to Process.Kill
            try
            {
                using var process = Process.GetProcessById(_pid);
                process.Kill();
                process.WaitForExit(3000);
            }
            catch
            {
                // Process may already be gone
            }
        }

        // Small delay to let the port free up
        System.Threading.Thread.Sleep(500);
        _page.Refresh();
        return CommandResult.KeepOpen();
    }
}
