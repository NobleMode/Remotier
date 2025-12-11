using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using Remotier.Models;
using Remotier.Services.Network;
using System.Net;
using System.Net.Sockets;

using System.Runtime.InteropServices;

namespace Remotier.Services;

public class HostService : IDisposable
{
    private TcpHost _tcpHost = null!;
    private InputService _inputService = null!;
    private UdpStreamSender _streamSender = null!;
    private DiscoveryService? _discoveryService;
    private ClipboardService? _clipboardService;
    private FileTransferService? _fileTransferService;
    private PortMappingService? _portMappingService;

    public bool IsPortMappingEnabled { get; private set; }
    public string PortMappingStatus { get; private set; } = "Disabled";

    private bool _isHosting;
    private Task? _captureTask; // Made nullable

    public event Action<string>? ClientConnected;
    public event Action<string>? ClientDisconnected;
    public event Action<double>? OnCaptureTiming; // Capture+Encode time in ms
    public event Action<int>? OnFpsUpdate;

    // P/Invoke Definitions
    private static class NativeMethods
    {
        private const string DllName = "Native.dll";

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int Init(int monitorIndex);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int CaptureAndEncode(int scalePercent, int quality, out IntPtr outData, out int outSize);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void Release();
    }

    public void Start(int port, StreamOptions options, int monitorIndex, int fps)
    {
        if (_isHosting) return;
        _currentPort = port;

        // Initialize Native Core
        int result = NativeMethods.Init(monitorIndex);
        if (result != 0)
        {
            Debug.WriteLine($"Native Init Failed: {result}");
            throw new Exception($"Native Init Failed: {result}");
        }

        _scalePercent = options.Quality;
        if (_scalePercent <= 0) _scalePercent = 100;

        _streamSender = new UdpStreamSender();

        _tcpHost = new TcpHost();
        _tcpHost.OnControlReceived += OnControlPacket;
        _tcpHost.ClientConnected += (client) =>
        {
            Interlocked.Increment(ref _connectedCount);
            ClientConnected?.Invoke(client.Client.RemoteEndPoint?.ToString() ?? "Unknown");
        };
        _tcpHost.ClientDisconnected += (client) =>
        {
            Interlocked.Decrement(ref _connectedCount);
            ClientDisconnected?.Invoke(client.Client.RemoteEndPoint?.ToString() ?? "Unknown");
        };
        _tcpHost.Start(port);

        _discoveryService = new DiscoveryService();
        _discoveryService.StartBeacon(port);

        _tcpHost.OnChatReceived += OnChatReceived;

        // Clipboard & File Transfer Integration
        _clipboardService = new ClipboardService();

        _fileTransferService = new FileTransferService();
        _fileTransferService.TransferCompleted += (path) =>
        {
            Debug.WriteLine($"File Received: {path}");
            // Ideally notify UI
        };

        // We need to track the current client to send data to it.
        _tcpHost.ClientConnected += (client) =>
        {
            _currentClient = client;
        };
        _tcpHost.ClientDisconnected += (client) =>
        {
            if (_currentClient == client) _currentClient = null;
        };

        _clipboardService.ClipboardTextChanged += async (text) =>
        {
            if (_currentClient != null)
                await _tcpHost.SendClipboardToClient(_currentClient, text);
        };

        _tcpHost.OnClipboardReceived += (text, client) => _clipboardService.SetClipboardText(text);

        _tcpHost.OnFileStartReceived += (name, size, client) => _fileTransferService.StartReceiving(name, size);
        _tcpHost.OnFileChunkReceived += (chunk, client) => _fileTransferService.ReceiveChunk(chunk);
        _tcpHost.OnFileEndReceived += (client) => _fileTransferService.FinishReceiving();

        _clipboardService.Start();

        _portMappingService = new PortMappingService();
        _portMappingService.StatusChanged += (status) => PortMappingStatus = status;

        _inputService = new InputService();
        _isHosting = true;

        _captureTask = Task.Run(() => CaptureLoop(fps));
    }

    private async Task CaptureLoop(int fps)
    {
        int targetDelay = 1000 / fps;

        while (_isHosting)
        {
            if (_connectedCount <= 0)
            {
                await Task.Delay(200);
                continue;
            }

            var sw = Stopwatch.StartNew();

            IntPtr pData;
            int size;

            int jpegQuality = 70;
            if (_scalePercent >= 90) jpegQuality = 85;
            else if (_scalePercent >= 70) jpegQuality = 75;
            else jpegQuality = 65;

            var captureSw = Stopwatch.StartNew();
            int result = NativeMethods.CaptureAndEncode(_scalePercent, jpegQuality, out pData, out size);
            captureSw.Stop();
            OnCaptureTiming?.Invoke(captureSw.Elapsed.TotalMilliseconds);

            if (result == 1 && size > 0)
            {
                byte[] data = new byte[size];
                Marshal.Copy(pData, data, 0, size);

                _frameCounter++;
                _streamSender.SendFrame(data);
            }

            long elapsedTicks = sw.ElapsedTicks;
            long targetTicks = Stopwatch.Frequency / fps;
            long remainingTicks = targetTicks - elapsedTicks;

            if (remainingTicks > 0)
            {
                long msToSleep = (remainingTicks / 10000) - 2;
                if (msToSleep > 0) await Task.Delay((int)msToSleep);
                while (sw.ElapsedTicks < targetTicks) Thread.SpinWait(100);
            }
        }
    }

    private void OnControlPacket(ControlPacket packet, TcpClient client)
    {
        if (packet.Type == PacketType.Connect)
        {
            if (client.Client.RemoteEndPoint is IPEndPoint remoteIp)
            {
                int targetPort = packet.Data > 0 ? packet.Data : 5000;
                string msg = $"Client Connected from {remoteIp.Address}:{targetPort}";
                Debug.WriteLine(msg);
                _streamSender.Connect(remoteIp.Address.ToString(), targetPort);
            }
        }
        else if (packet.Type == PacketType.Mouse || packet.Type == PacketType.Keyboard)
        {
            _inputService.HandleInput(packet);
        }
        else if (packet.Type == PacketType.Settings)
        {
            if (packet.X > 0 && packet.Y > 0)
            {
                _clientWidth = packet.X;
                _clientHeight = packet.Y;
            }
        }
    }

    public void UpdateSettings(int scalePercent)
    {
        _scalePercent = Math.Clamp(scalePercent, 10, 100);
    }

    private int _clientWidth = 0;
    private int _clientHeight = 0;
    private int _scalePercent = 100;
    private int _connectedCount = 0;
    private TcpClient? _currentClient; // Track active client
    private int _frameCounter = 0;

    // Chat
    public event Action<string>? ChatReceived;

    private void OnChatReceived(string message, TcpClient client)
    {
        // Passthrough to UI
        System.Windows.Application.Current.Dispatcher.Invoke(() => ChatReceived?.Invoke(message));
    }

    public void SendChat(string message)
    {
        _tcpHost?.BroadcastChat(message);
    }

    public void Stop()
    {
        _isHosting = false;
        try
        {
            if (_captureTask != null && !_captureTask.IsCompleted) _captureTask.Wait(500);
        }
        catch { }

        NativeMethods.Release();

        _clipboardService?.Stop();
        _clipboardService = null;
        _fileTransferService = null;

        _discoveryService?.Stop();
        _discoveryService = null;

        if (_portMappingService != null)
        {
            _ = _portMappingService.StopMapping();
            _portMappingService = null;
        }

        _tcpHost?.Stop();
        _streamSender?.Dispose();
        // _frameQueue?.Dispose(); // Removed
    }

    public async Task SendFile(string path)
    {
        if (_currentClient == null || !_currentClient.Connected) return;

        if (_fileTransferService != null)
        {
            var fileName = System.IO.Path.GetFileName(path);
            var fileInfo = new System.IO.FileInfo(path);

            await _tcpHost.SendFileStartToClient(_currentClient, fileName, fileInfo.Length);

            await _fileTransferService.SendFile(path,
                async (chunk) => await _tcpHost.SendFileChunkToClient(_currentClient, chunk),
                async () => await _tcpHost.SendFileEndToClient(_currentClient));
        }
    }

    public async Task TogglePortMapping(bool enable)
    {
        IsPortMappingEnabled = enable;
        if (_portMappingService == null) return;

        if (enable)
        {
            // Use same port as Host
            // We need to know the port. It was passed to Start().
            // Ideally store it in a field or pass it here.
            // For now, let's assume default 5000 or store it in Start.
            // Let's modify Start to store_port.
            await _portMappingService.StartMapping(_currentPort);
        }
        else
        {
            await _portMappingService.StopMapping();
        }
    }

    private int _currentPort = 5000; // Default

    public void Dispose()
    {
        Stop();
    }
}
