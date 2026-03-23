# Port Manager — Command Palette Extension

A [PowerToys Command Palette](https://learn.microsoft.com/windows/powertoys/command-palette/overview) extension that shows active TCP listeners on common development ports and lets you stop them with one click.

## Features

- 🔍 Scans for active TCP listeners across common dev ports (3000, 5000, 5173, 8080, etc.)
- 🏷️ Tags known development ports for easy identification
- 🛑 Stop any process directly from the Command Palette
- 🔄 Auto-refreshes the list after stopping a process

## Getting Started

### Prerequisites

- Windows 11 with [PowerToys](https://github.com/microsoft/PowerToys) installed
- [Developer mode](https://learn.microsoft.com/windows/advanced-settings/developer-mode) enabled
- Visual Studio with C# and WinUI workloads

### Build & Deploy

1. Open `PortManager.sln` in Visual Studio
2. Build → Deploy PortManager
3. In Command Palette, run **Reload** (Reload Command Palette extensions)
4. Search for **Port Manager**

## Usage

1. Open Command Palette
2. Select **Port Manager**
3. View all active port listeners with process names and PIDs
4. Select any entry to stop that process

## License

MIT
