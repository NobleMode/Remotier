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

namespace Remotier.Services;

public class HostService : IDisposable
{
    private TcpHost _tcpHost = null!;
    private InputService _inputService = null!;
    private CaptureService _captureService = null!;
    private CompressionService _compressionService = null!;
    private UdpStreamSender _streamSender = null!;

    private bool _isHosting;
    private Task _captureTask;
    private Task _processTask;

    // Pipeline buffer: Limit to 2 frames to avoid latency buildup
    private BlockingCollection<Bitmap> _frameQueue = new BlockingCollection<Bitmap>(2);

    public event Action<string> ClientConnected = delegate { };
    public event Action<string> ClientDisconnected = delegate { };

    public void Start(int port, StreamOptions options, int monitorIndex, int fps)
    {
        _captureService = new CaptureService();
        _captureService.Initialize(monitorIndex);

        _compressionService = new CompressionService(options);

        // Use the initial quality value as scale percent if implicit, or just init to 100
        // The HostWindow passes options.Quality which we interpret as scale.
        _scalePercent = options.Quality;
        if (_scalePercent <= 0) _scalePercent = 100;

        _streamSender = new UdpStreamSender();

        _tcpHost = new TcpHost();
        _tcpHost.OnControlReceived += OnControlPacket;
        _tcpHost.ClientConnected += (client) =>
        {
            Interlocked.Increment(ref _connectedCount);
            ClientConnected?.Invoke(client.Client.RemoteEndPoint.ToString());
        };
        _tcpHost.ClientDisconnected += (client) =>
        {
            Interlocked.Decrement(ref _connectedCount);
            ClientDisconnected?.Invoke(client.Client.RemoteEndPoint?.ToString() ?? "Unknown");
        };
        _tcpHost.Start(port);

        _inputService = new InputService();
        _isHosting = true;

        _captureTask = Task.Run(() => CaptureLoop(fps));
        _processTask = Task.Run(ProcessLoop);
    }

    private void OnControlPacket(ControlPacket packet, TcpClient client)
    {
        if (packet.Type == PacketType.Connect)
        {
            if (client.Client.RemoteEndPoint is IPEndPoint remoteIp)
            {
                // Use the port sent in Data, or default to 5000 if 0
                int targetPort = packet.Data > 0 ? packet.Data : 5000;
                string msg = $"Client Connected from {remoteIp.Address}:{targetPort}";
                Debug.WriteLine(msg);
                Console.WriteLine(msg);
                _streamSender.Connect(remoteIp.Address.ToString(), targetPort);
            }
        }
        else if (packet.Type == PacketType.Mouse || packet.Type == PacketType.Keyboard)
        {
            _inputService.HandleInput(packet);
        }
        else if (packet.Type == PacketType.Settings)
        {
            // X = Width, Y = Height
            if (packet.X > 0 && packet.Y > 0)
            {
                _clientWidth = packet.X;
                _clientHeight = packet.Y;
                RecalculateResolution();
            }
        }
    }

    // Temporary fix: Method to set client IP explicitly if needed, or we rely on TCP Host modification.
    public void SetClientIp(string ip, int port)
    {
        _streamSender.Connect(ip, port);
    }

    private int _clientWidth = 0;
    private int _clientHeight = 0;
    private int _scalePercent = 100;

    public void UpdateSettings(int scalePercent)
    {
        _scalePercent = Math.Clamp(scalePercent, 10, 100);
        RecalculateResolution();
    }

    private void RecalculateResolution()
    {
        // If we don't know client size, default to 100% of source or just don't scale (or use fixed scale)
        if (_clientWidth == 0 || _clientHeight == 0)
        {
            // Fallback: If user wants scaling but no client connected yet, 
            // we can't really scale to target. Just disable scaling or use percentage of Source?
            // Since CaptureService gives us Source Size, we can use that.
            if (_captureService != null)
            {
                int w = _captureService.ScreenWidth * _scalePercent / 100;
                int h = _captureService.ScreenHeight * _scalePercent / 100;
                _compressionService?.SetScaling(true, w, h);
            }
            return;
        }

        int targetW = _clientWidth * _scalePercent / 100;
        int targetH = _clientHeight * _scalePercent / 100;

        // Ensure at least 1x1
        targetW = Math.Max(1, targetW);
        targetH = Math.Max(1, targetH);

        Console.WriteLine($"Updating Resolution: Client={_clientWidth}x{_clientHeight}, Scale={_scalePercent}% -> Target={targetW}x{targetH}");
        _compressionService?.SetScaling(true, targetW, targetH);
    }

    private int _connectedCount = 0;
    private int _frameCounter = 0;

    private async Task CaptureLoop(int fps)
    {
        int targetDelay = 1000 / fps;
        while (_isHosting)
        {
            // Optimization: Don't capture if no clients are connected
            if (_connectedCount <= 0)
            {
                await Task.Delay(200);
                continue;
            }

            var sw = Stopwatch.StartNew();

            // 1. Capture Frame (GPU/Driver bound)
            var bitmap = _captureService.CaptureFrame();

            if (bitmap != null)
            {
                // 2. Push to Pipeline (Non-blocking drop if full to prioritize latest)
                if (!_frameQueue.TryAdd(bitmap))
                {
                    // Queue full, drop this frame to avoid latency
                    bitmap.Dispose();
                }
            }
            else
            {
                // Console.WriteLine("CaptureFrame returned null"); 
            }

            // Smart Delay to maintain target framerate of ~60 FPS
            sw.Stop();
            int elapsed = (int)sw.ElapsedMilliseconds;
            int delay = Math.Max(0, targetDelay - elapsed);
            await Task.Delay(delay);
        }

        _frameQueue.CompleteAdding();
    }

    private void ProcessLoop()
    {
        foreach (var bitmap in _frameQueue.GetConsumingEnumerable())
        {
            try
            {
                byte[] data = _compressionService.Compress(bitmap);
                if (data != null)
                {
                    _frameCounter++;
                    if (_frameCounter % 60 == 0) Console.WriteLine($"Sending frame {_frameCounter} ({data.Length} bytes)");
                    _streamSender.SendFrame(data);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error processing frame: {ex.Message}");
            }
            finally
            {
                bitmap.Dispose();
            }
        }
    }

    public void Stop()
    {
        _isHosting = false;

        // Wait for capture task to complete to avoid AccessViolation in CaptureService
        try
        {
            if (_captureTask != null && !_captureTask.IsCompleted)
            {
                // Wait up to 500ms for graceful exit
                _captureTask.Wait(500);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error waiting for capture task: {ex.Message}");
        }

        _tcpHost?.Stop();
        _captureService?.Dispose();
        _streamSender?.Dispose();
        _frameQueue?.Dispose();

        // Re-init frame queue for next run if needed, but we create new HostService anyway.
    }

    public void Dispose()
    {
        Stop();
    }
}
