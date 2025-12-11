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

using System.Security.Cryptography;
using System.Text;

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

    public string SessionId { get; private set; } = "";
    public string SessionPassword { get; private set; } = "";
    public SecuritySettings SecuritySettings { get; private set; } = new SecuritySettings();

    private bool _isHosting;
    private Task? _captureTask; // Made nullable

    public event Action<string>? ClientConnected;
    public event Action<string>? ClientDisconnected;
    public event Action<double>? OnCaptureTiming; // Capture+Encode time in ms
    public event Action<int>? OnFpsUpdate;
    public event Func<string, long, bool>? RequestFileAcceptance;

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

        // Security Init
        ReloadSettings();
        GenerateSessionCredentials();

        _tcpHost = new TcpHost();
        _tcpHost.VerifyClientAuth += VerifyAuth;
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
        _tcpHost.OnAuthenticationFailed += (reason) =>
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() => OnAuthenticationFailed?.Invoke(reason));
        };

        // Clipboard & File Transfer Integration
        _clipboardService = new ClipboardService();

        _fileTransferService = new FileTransferService();
        _fileTransferService.TransferCompleted += (path) =>
        {
            Debug.WriteLine($"File Received: {path}");
            // Ideally notify UI
        };

        // Wire up security event
        _fileTransferService.RequestFileAcceptance += (name, size) =>
        {
            // Bubble up to UI via HostService event
            // We need a Synchronous mechanism. 
            // Since HostService runs on a background thread (usually), but the UI needs to show a MessageBox.
            // We can invoke a delegate that returns bool.
            if (RequestFileAcceptance != null)
            {
                // We only support one handler (the UI)
                foreach (Func<string, long, bool> handler in RequestFileAcceptance.GetInvocationList())
                {
                    return handler(name, size);
                }
            }
            return false; // Default deny
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

    public void ReloadSettings()
    {
        SecuritySettings = SecuritySettings.Load();
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

    public void DisconnectClient()
    {
        if (_currentClient != null)
        {
            try
            {
                _currentClient.Close();
                _currentClient = null;
            }
            catch { }
        }
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
    public void GenerateSessionCredentials()
    {
        SessionId = Guid.NewGuid().ToString("N").Substring(0, 9).ToUpper().Insert(3, "-").Insert(7, "-");
        SessionPassword = new Random().Next(0, 999999).ToString("D6");
        // Notify UI if needed? Properties are public, UI can poll or bind? 
        // We might need INotifyPropertyChanged or an event.
        // For now, HostWindow just reads it on startup/refresh.
    }

    public event Action<string> OnAuthenticationFailed = delegate { };

    public (bool isAuthenticated, bool isTrusted, string failureReason) VerifyAuth(string deviceId, string deviceName, string accountName, string inputHash, string salt)
    {
        string specificError = "Invalid Session Password";

        // 1. Check Account Password (Trusted)
        // Only attempt if Client provided an Account Name AND Host has an Account Name set.
        if (!string.IsNullOrEmpty(SecuritySettings.AccountName) &&
            !string.IsNullOrEmpty(accountName))
        {
            if (accountName == SecuritySettings.AccountName)
            {
                if (!string.IsNullOrEmpty(SecuritySettings.AccountPasswordHash))
                {
                    string expected = ComputeSha256(SecuritySettings.AccountPasswordHash + salt);
                    if (inputHash == expected)
                    {
                        RegisterConnection(deviceId, deviceName, true);
                        return (true, true, "");
                    }
                    // If name matches but password wrong, that's a specific error. 
                    // But maybe they put Session Password in the password field?
                    // Let's NOT return immediately, let's try Session Password too.
                    specificError = "Trusted: Bad Password";
                }
                else
                {
                    specificError = "Trusted: No Host Password Set";
                }
            }
            else
            {
                specificError = $"Account '{accountName}' not found";
            }
        }

        // 2. Check Session Password (Guest)
        // If they failed Trusted (or didn't provide name), try Guest.
        // This handles the case where user puts "Session ID" in Account Name field by mistake.

        string sessionBaseHash = ComputeSha256(SessionPassword);
        string expectedSession = ComputeSha256(sessionBaseHash + salt);

        if (inputHash == expectedSession)
        {
            RegisterConnection(deviceId, deviceName, false);
            return (true, false, "");
        }

        // If we got here, both failed.

        string debugDetails = $"[InputAcc: '{accountName}' HostAcc: '{SecuritySettings.AccountName}'] [Salt: {salt.Substring(0, 5)}...] [Recv: {(inputHash.Length > 5 ? inputHash.Substring(0, 5) : inputHash)}... Exp: {(expectedSession.Length > 5 ? expectedSession.Substring(0, 5) : expectedSession)}...]";

        return (false, false, (specificError != "Invalid Session Password" ? $"{specificError} OR Invalid Session Password" : specificError) + debugDetails);
    }

    private void RegisterConnection(string deviceId, string deviceName, bool isTrusted)
    {
        // Update Recent
        var recent = SecuritySettings.RecentConnections.Find(x => x.DeviceName == deviceName); // Using Name as ID for Display? 
                                                                                               // We should store DeviceID in History too?
                                                                                               // Let's just add new entry for now.
        SecuritySettings.RecentConnections.RemoveAll(x => x.DeviceName == deviceName && x.IpAddress == deviceId); // Cleanup old ?
        SecuritySettings.RecentConnections.Insert(0, new ConnectionHistory
        {
            DeviceName = deviceName,
            IpAddress = "Unknown", // We don't have IP easily here yet, passed from TCP?
            ConnectedAt = DateTime.Now
        });
        if (SecuritySettings.RecentConnections.Count > 10) SecuritySettings.RecentConnections.RemoveAt(10);

        // Update Trusted
        if (isTrusted)
        {
            var existing = SecuritySettings.TrustedDevices.Find(d => d.DeviceId == deviceId);
            if (existing == null)
            {
                SecuritySettings.TrustedDevices.Add(new DeviceInfo
                {
                    DeviceId = deviceId,
                    DeviceName = deviceName,
                    LastSeen = DateTime.Now
                });
            }
            else
            {
                existing.LastSeen = DateTime.Now;
                existing.DeviceName = deviceName;
            }
        }

        SecuritySettings.Save();
    }

    public static string ComputeSha256(string input)
    {
        using (SHA256 sha256 = SHA256.Create())
        {
            byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));
            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < bytes.Length; i++)
            {
                builder.Append(bytes[i].ToString("x2"));
            }
            return builder.ToString();
        }
    }
}
