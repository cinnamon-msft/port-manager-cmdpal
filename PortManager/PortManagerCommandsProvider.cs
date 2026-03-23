using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace PortManager;

public sealed partial class PortManagerCommandsProvider : CommandProvider
{
    private readonly ICommandItem[] _commands;

    public PortManagerCommandsProvider()
    {
        DisplayName = "Port Manager";
        Icon = new IconInfo("\uE968"); // NetworkTower icon
        _commands =
        [
            new CommandItem(new PortManagerPage()) { Title = "Port Manager", Subtitle = "View and stop processes on dev ports" },
        ];
    }

    public override ICommandItem[] TopLevelCommands() => _commands;
}
