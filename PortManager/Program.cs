using Microsoft.CommandPalette.Extensions;
using Shmuelie.WinRTServer;

namespace PortManager;

public static class Program
{
    [MTAThread]
    public static void Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "-RegisterProcessAsComServer")
        {
            var server = new ComServer();
            server.RegisterClass<PortManager, IExtension>();
            server.Start();

            // Keep the process alive until terminated by the host
            Thread.Sleep(Timeout.Infinite);
        }
    }
}
