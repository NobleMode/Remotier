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

    public void Start(int port, StreamOptions options)
    {
        _captureService = new CaptureService();
        _captureService.Initialize();

        _compressionService = new CompressionService(options);

        _streamSender = new UdpStreamSender();

        _tcpHost = new TcpHost();
        _tcpHost.OnControlReceived += OnControlPacket;
        _tcpHost.Start(port);

        _inputService = new InputService();
        _isHosting = true;

        _captureTask = Task.Run(CaptureLoop);
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
    }

    // Temporary fix: Method to set client IP explicitly if needed, or we rely on TCP Host modification.
    public void SetClientIp(string ip, int port)
    {
        _streamSender.Connect(ip, port);
    }

    private int _frameCounter = 0;

    private async Task CaptureLoop()
    {
        while (_isHosting)
        {
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
            int delay = Math.Max(0, 16 - elapsed);
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
        _tcpHost?.Stop();
        _captureService?.Dispose();
        _streamSender?.Dispose();
        _frameQueue?.Dispose();
    }

    public void Dispose()
    {
        Stop();
    }
}
