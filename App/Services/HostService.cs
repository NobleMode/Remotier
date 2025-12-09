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

    private bool _isHosting;
    private Task? _captureTask; // Made nullable

    public event Action<string>? ClientConnected;
    public event Action<string>? ClientDisconnected;

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

            int result = NativeMethods.CaptureAndEncode(_scalePercent, jpegQuality, out pData, out size);

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

        _discoveryService?.Stop();
        _discoveryService = null;

        _tcpHost?.Stop();
        _streamSender?.Dispose();
        // _frameQueue?.Dispose(); // Removed
    }

    public void Dispose()
    {
        Stop();
    }
}
