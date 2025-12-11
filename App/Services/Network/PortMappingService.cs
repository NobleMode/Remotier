using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Open.Nat;

namespace Remotier.Services.Network;

public class PortMappingService
{
    private NatDevice? _device;
    private Mapping? _tcpMapping;
    private Mapping? _udpMapping;
    private bool _isMapping;
    private CancellationTokenSource? _cts;

    public IPAddress? ExternalIpAddress { get; private set; }
    public event Action<string> StatusChanged = delegate { };

    public async Task StartMapping(int port)
    {
        if (_isMapping) return;
        _isMapping = true;
        _cts = new CancellationTokenSource();
        _cts.CancelAfter(TimeSpan.FromSeconds(10)); // 10s timeout for discovery

        try
        {
            StatusChanged?.Invoke("Discovering UPnP device...");
            var discoverer = new NatDiscoverer();

            // Discover device
            _device = await discoverer.DiscoverDeviceAsync(PortMapper.Upnp, _cts);

            if (_device == null)
            {
                StatusChanged?.Invoke("No UPnP device found.");
                _isMapping = false;
                return;
            }

            StatusChanged?.Invoke("Retrieving External IP...");
            ExternalIpAddress = await _device.GetExternalIPAsync();

            StatusChanged?.Invoke($"Mapping port {port}...");

            // Create Mappings
            _tcpMapping = new Mapping(Protocol.Tcp, port, port, "Remotier Host (TCP)");
            _udpMapping = new Mapping(Protocol.Udp, port, port, "Remotier Host (UDP)");

            await _device.CreatePortMapAsync(_tcpMapping);
            await _device.CreatePortMapAsync(_udpMapping);

            StatusChanged?.Invoke($"Success! External IP: {ExternalIpAddress}:{port}");
        }
        catch (NatDeviceNotFoundException)
        {
            StatusChanged?.Invoke("No UPnP device found.");
            _isMapping = false;
        }
        catch (MappingException ex)
        {
            StatusChanged?.Invoke($"Mapping failed: {ex.Message}");
            _isMapping = false;
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke($"Error: {ex.Message}");
            _isMapping = false;
        }
    }

    public async Task StopMapping()
    {
        if (_device != null)
        {
            try
            {
                if (_tcpMapping != null) await _device.DeletePortMapAsync(_tcpMapping);
                if (_udpMapping != null) await _device.DeletePortMapAsync(_udpMapping);
            }
            catch { }
            _device = null;
            _tcpMapping = null;
            _udpMapping = null;
        }

        _isMapping = false;
        ExternalIpAddress = null;
        StatusChanged?.Invoke("Port mapping disabled.");
    }
}
