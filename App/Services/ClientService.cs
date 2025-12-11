using System;
using System.Drawing;
using System.Windows.Media.Imaging;
using System.Threading.Tasks;
using System.Threading;
using System.Collections.Concurrent;
using System.Diagnostics;
using Remotier.Services.Network;
using Remotier.Services.Utils;

namespace Remotier.Services;

public class ClientService : IDisposable
{
    private UdpStreamReceiver? _receiver;
    private TcpControlClient? _tcpClient;
    private ClipboardService? _clipboardService;
    private FileTransferService? _fileTransferService;

    public event Action<BitmapImage>? OnFrameReady;
    public event Action<double>? OnFrameTiming; // Expose timing (Decode Time)
    public event Action? Reconnecting;
    public event Action? Reconnected;
    public event Action? Disconnected;

    private string? _lastIp;
    private int _lastPort;
    private string _lastAccountName = "";
    private string _lastPassword = "";
    private bool _isReconnecting;
    private bool _intentionalDisconnect;

    // Auto-Latency Correction
    private BlockingCollection<byte[]>? _frameQueue;
    private CancellationTokenSource? _frameProcessingCts;
    private Task? _frameProcessingTask;

    public async Task ConnectAsync(string ip, int port, string accountName = "", string password = "")
    {
        _lastIp = ip;
        _lastPort = port;
        _lastAccountName = accountName;
        _lastPassword = password;
        _intentionalDisconnect = false;

        StartFrameProcessing();

        await InitializeConnection(ip, port);
    }

    private void StartFrameProcessing()
    {
        if (_frameProcessingTask != null && !_frameProcessingTask.IsCompleted) return;

        _frameQueue = new BlockingCollection<byte[]>(boundedCapacity: 2); // Small buffer to keep latency low
        _frameProcessingCts = new CancellationTokenSource();
        _frameProcessingTask = Task.Run(ProcessFrames, _frameProcessingCts.Token);
    }

    private void ProcessFrames()
    {
        if (_frameQueue == null || _frameProcessingCts == null) return;

        try
        {
            foreach (var data in _frameQueue.GetConsumingEnumerable(_frameProcessingCts.Token))
            {
                var sw = Stopwatch.StartNew();
                // Decompress
                using var bitmap = CompressionService.Decompress(data);
                if (bitmap != null)
                {
                    // Convert System.Drawing.Bitmap to WPF BitmapImage
                    var image = ScreenUtils.BitmapToImageSource(bitmap);
                    if (image != null)
                    {
                        image.Freeze(); // Make it cross-thread accessible
                        OnFrameReady?.Invoke(image);
                    }
                }
                sw.Stop();
                OnFrameTiming?.Invoke(sw.Elapsed.TotalMilliseconds);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Debug.WriteLine($"Frame Processing Error: {ex.Message}");
        }
    }

    private async Task InitializeConnection(string? ip, int port)
    {
        if (string.IsNullOrEmpty(ip)) throw new ArgumentNullException(nameof(ip));
        // Cleanup existing if any (e.g. during reconnect)
        DisposeResources(false); // Don't stop processing loop on reconnect

        _receiver = new UdpStreamReceiver();
        _receiver.OnFrameReceived += OnFrameReceived;
        _receiver.Start(port);

        _tcpClient = new TcpControlClient();
        _tcpClient.ConnectionLost += OnConnectionLost;
        _tcpClient.ConnectionLost += OnConnectionLost;
        _tcpClient.ChatReceived += (msg) => ChatReceived?.Invoke(msg);

        // Clipboard & File Transfer
        _clipboardService = new ClipboardService();
        _clipboardService.ClipboardTextChanged += async (text) => await _tcpClient.SendClipboard(text);
        _clipboardService.Start();

        _fileTransferService = new FileTransferService();
        _fileTransferService.TransferCompleted += (path) =>
        {
            // Notify UI? For now debug
            Debug.WriteLine($"Client Received File: {path}");
        };

        _tcpClient.ClipboardReceived += (text) => _clipboardService.SetClipboardText(text);
        _tcpClient.FileStartReceived += (name, size) => _fileTransferService.StartReceiving(name, size);
        _tcpClient.FileChunkReceived += (chunk) => _fileTransferService.ReceiveChunk(chunk);
        _tcpClient.FileEndReceived += () => _fileTransferService.FinishReceiving();

        try
        {
            await _tcpClient.ConnectAsync(ip, port, _lastAccountName, _lastPassword);

            // Send Connect Packet
            var packet = new ControlPacket { Type = PacketType.Connect, Data = port };
            _tcpClient.SendControl(packet);
        }
        catch
        {
            // If initial connect fails, rethrow so UI knows
            DisposeResources(true);
            throw;
        }
    }

    private void OnConnectionLost()
    {
        if (_intentionalDisconnect) return;

        // Trigger Auto-Reconnect
        _ = ReconnectLoop();
    }

    private async Task ReconnectLoop()
    {
        if (_isReconnecting) return;
        _isReconnecting = true;
        Reconnecting?.Invoke();

        while (!_intentionalDisconnect)
        {
            try
            {
                await InitializeConnection(_lastIp, _lastPort);

                // Re-connected!
                _isReconnecting = false;
                Reconnected?.Invoke();
                return;
            }
            catch
            {
                // Wait and try again
                await Task.Delay(3000);
            }
        }

        _isReconnecting = false;
    }

    private void OnFrameReceived(byte[] data)
    {
        if (_frameQueue == null || _frameQueue.IsAddingCompleted) return;

        // Auto-Latency Correction:
        // If the queue is becoming full, it means the consumer (decoder) is slower than the producer (network).
        // We drop the oldest frames to ensure we always processing current data (Live).
        // Since Capacity is 2, if count is >=1 or full, we try to make space.
        while (_frameQueue.Count >= 1)
        {
            _frameQueue.TryTake(out _);
        }

        _frameQueue.TryAdd(data);
    }

    public long TotalBytesReceived => _receiver?.TotalBytesReceived ?? 0;

    public void SendInput(ControlPacket packet)
    {
        _tcpClient?.SendControl(packet);
    }

    public void SendChat(string message)
    {
        _tcpClient?.SendChat(message);
    }

    public async Task SendFile(string path)
    {
        if (_tcpClient == null) return;

        if (_fileTransferService != null)
        {
            var fileName = System.IO.Path.GetFileName(path);
            var fileInfo = new System.IO.FileInfo(path);

            await _tcpClient.SendFileStart(fileName, fileInfo.Length);

            await _fileTransferService.SendFile(path,
                async (chunk) => await _tcpClient.SendFileChunk(chunk),
                async () => await _tcpClient.SendFileEnd());
        }
    }

    public event Action<string> ChatReceived = delegate { };

    private void DisposeResources(bool fullStop = false)
    {
        if (_tcpClient != null)
        {
            _tcpClient.ConnectionLost -= OnConnectionLost;
            _tcpClient.Dispose();
            _tcpClient = null;
        }
        _receiver?.Dispose();
        _receiver = null;

        _clipboardService?.Stop();
        _clipboardService = null;
        _fileTransferService = null;

        if (fullStop)
        {
            _frameProcessingCts?.Cancel();
            _frameQueue?.CompleteAdding();
            // Maybe wait? but we are on UI thread usually or async.
        }
    }

    public void Dispose()
    {
        _intentionalDisconnect = true;
        DisposeResources(true);
    }
}
