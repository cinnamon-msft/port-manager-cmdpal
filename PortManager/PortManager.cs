using Microsoft.CommandPalette.Extensions;
using System.Runtime.InteropServices;

namespace PortManager;

[ComVisible(true)]
[Guid("2E6C7B3A-4F1D-4A8E-9C5B-D3E7F2A1B6C0")]
[ComDefaultInterface(typeof(IExtension))]
public sealed partial class PortManager : IExtension, IDisposable
{
    private readonly ManualResetEvent _extensionDisposedEvent;
    private readonly PortManagerCommandsProvider _provider = new();

    public PortManager(ManualResetEvent extensionDisposedEvent)
    {
        _extensionDisposedEvent = extensionDisposedEvent;
    }

    public object GetProvider(ProviderType providerType)
    {
        return providerType switch
        {
            ProviderType.Commands => _provider,
            _ => null!,
        };
    }

    public void Dispose()
    {
        _extensionDisposedEvent.Set();
    }
}
