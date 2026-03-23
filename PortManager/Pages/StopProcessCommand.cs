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
            using var process = Process.GetProcessById(_pid);
            process.Kill(entireProcessTree: true);
        }
        catch (ArgumentException)
        {
            // Process already exited
        }
        catch (Exception ex)
        {
            Debug.Write($"Failed to stop PID {_pid}: {ex.Message}");
        }

        _page.Refresh();
        return CommandResult.KeepOpen();
    }
}
