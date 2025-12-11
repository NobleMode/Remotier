using System.Windows;
using System.Windows.Input;
using Remotier.Models;
using Remotier.Services;
using Remotier.Services.Network;
using NetworkMouseAction = Remotier.Services.Network.MouseAction;

namespace Remotier;

public partial class RemoteViewWindow : Window
{
    private ClientService _clientService;
    private ConnectionInfo _info;

    public RemoteViewWindow(ConnectionInfo info, ClientService clientService = null)
    {
        InitializeComponent();
        _info = info;
        _clientService = clientService; // Use provided service or null
        Loaded += RemoteViewWindow_Loaded;
        Closing += RemoteViewWindow_Closing;
    }

    private bool _inputEnabled = true;
    private int _frameCount = 0;
    private DateTime _lastUpdate = DateTime.Now;

    private void ToggleInput_Click(object sender, RoutedEventArgs e)
    {
        _inputEnabled = !_inputEnabled;
        var btn = sender as System.Windows.Controls.Button;
        if (btn != null)
        {
            btn.Content = _inputEnabled ? "Toggle Input" : "Input Disabled";
            btn.Background = _inputEnabled ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(68, 68, 68)) : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(100, 100, 100));
        }
    }

    private void Disconnect_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void ToggleFullscreen_Click(object sender, RoutedEventArgs e)
    {
        if (WindowStyle == WindowStyle.None)
        {
            WindowStyle = WindowStyle.SingleBorderWindow;
            WindowState = WindowState.Normal;
            if (FullscreenBtn != null) FullscreenBtn.Content = "Fullscreen";
        }
        else
        {
            WindowStyle = WindowStyle.None;
            WindowState = WindowState.Maximized;
            if (FullscreenBtn != null) FullscreenBtn.Content = "Windowed Mode";
        }
    }

    private long _lastBytesReceived = 0;

    private async void RemoteViewWindow_Loaded(object sender, RoutedEventArgs e)
    {
        DebugInfo.Text = $"Connected to {_info.IP}:{_info.Port}\n(As: '{_info.AccountName}')";

        // If service was strictly passed, it's already connected.
        // If null, we create and connect (Legacy/Direct mode)
        bool isNewConnection = false;
        if (_clientService == null)
        {
            _clientService = new ClientService();
            isNewConnection = true;
        }

        _clientService.Reconnecting += () => Dispatcher.Invoke(() =>
        {
            ReconnectingOverlay.Visibility = Visibility.Visible;
            DebugInfo.Text = "Reconnecting...";
        });

        _clientService.Reconnected += () => Dispatcher.Invoke(() =>
        {
            ReconnectingOverlay.Visibility = Visibility.Collapsed;
            DebugInfo.Text = "Connected";
        });

        _clientService.Disconnected += () => Dispatcher.Invoke(() =>
        {
            ReconnectingOverlay.Visibility = Visibility.Collapsed;
            DebugInfo.Text = "Disconnected";
            Close();
        });

        _clientService.OnFrameReady += (img) => Dispatcher.Invoke(() =>
        {
            ScreenImage.Source = img;
            _frameCount++;
            var now = DateTime.Now;
            var timeDiff = (now - _lastUpdate).TotalSeconds;
            if (timeDiff >= 1)
            {
                long currentBytes = _clientService.TotalBytesReceived;
                long bytesDiff = currentBytes - _lastBytesReceived;
                double mbps = (bytesDiff / 1024.0 / 1024.0) / timeDiff;

                StatsText.Text = $"FPS: {_frameCount} | Res: {img.PixelWidth}x{img.PixelHeight} | {mbps:F2} MB/s";

                _frameCount = 0;
                _lastUpdate = now;
                _lastBytesReceived = currentBytes;
            }
        });

        _clientService.OnFrameTiming += (ms) => Dispatcher.Invoke(() =>
        {
            if (TimingGraph.Visibility == Visibility.Visible)
            {
                TimingGraph.AddSample(ms);
            }
        });

        try
        {
            _clientService.ChatReceived += OnChatReceived;

            if (isNewConnection)
            {
                DebugInfo.Text = $"Connecting to {_info.IP}:{_info.Port}...\n(As: '{_info.AccountName}')";
                await _clientService.ConnectAsync(_info.IP, _info.Port, _info.AccountName, _info.Password);
                DebugInfo.Text = "Connected";
            }

            // Send initial resolution immediately after connection
            _clientService.SendInput(new ControlPacket
            {
                Type = PacketType.Settings,
                X = (int)ActualWidth,
                Y = (int)ActualHeight
            });

        }
        catch (Exception ex)
        {
            MessageBox.Show($"Connection failed: {ex.Message}\nCheck IP and Firewall on Host.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            Close();
        }
    }

    private ChatWindow? _chatWindow;

    private void ToggleToolbar_Click(object sender, RoutedEventArgs e)
    {
        if (ToolbarPanel.Visibility == Visibility.Visible)
        {
            ToolbarPanel.Visibility = Visibility.Collapsed;
            // ChevronIcon.Kind = Material.Icons.MaterialIconKind.ChevronDown; // Need to bind or find element
        }
        else
        {
            ToolbarPanel.Visibility = Visibility.Visible;
            // ChevronIcon.Kind = Material.Icons.MaterialIconKind.ChevronUp;
        }
    }

    private void Chat_Click(object sender, RoutedEventArgs e)
    {
        OpenChatWindow();
    }

    private async void SendFile_Click(object sender, RoutedEventArgs e)
    {
        var openFileDialog = new Microsoft.Win32.OpenFileDialog();
        if (openFileDialog.ShowDialog() == true)
        {
            try
            {
                DebugInfo.Visibility = Visibility.Visible;
                DebugInfo.Text = $"Sending {System.IO.Path.GetFileName(openFileDialog.FileName)}...";

                await _clientService.SendFile(openFileDialog.FileName);

                DebugInfo.Text = "File Sent!";
                await Task.Delay(2000);
                DebugInfo.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"File Send Error: {ex.Message}");
                DebugInfo.Visibility = Visibility.Collapsed;
            }
        }
    }

    private void OpenChatWindow()
    {
        if (_chatWindow == null || !_chatWindow.IsVisible)
        {
            _chatWindow = new ChatWindow();
            _chatWindow.Closed += (s, e) => _chatWindow = null;
            _chatWindow.MessageSent += (msg) => _clientService.SendChat(msg);
            _chatWindow.Show();
        }
        _chatWindow.Activate();
    }

    private void OnChatReceived(string message)
    {
        Dispatcher.Invoke(() =>
        {
            OpenChatWindow();
            _chatWindow?.AddMessage("Host", message);
            if (!_chatWindow.IsActive) _chatWindow.Activate();
        });
    }

    private void RemoteViewWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        _chatWindow?.Close();
        _clientService?.Dispose();
    }

    // Input Handling

    private (int x, int y) GetScaledPos(MouseEventArgs e)
    {
        var pos = e.GetPosition(ScreenImage);
        var actualWidth = ScreenImage.ActualWidth;
        var actualHeight = ScreenImage.ActualHeight;

        if (ScreenImage.Source == null) return ((int)pos.X, (int)pos.Y);

        double normX = pos.X / actualWidth;
        double normY = pos.Y / actualHeight;

        int absX = (int)(normX * 65535);
        int absY = (int)(normY * 65535);

        return (absX, absY);
    }

    private void ScreenImage_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_inputEnabled) return;
        var (x, y) = GetScaledPos(e);
        _clientService.SendInput(new ControlPacket
        {
            Type = PacketType.Mouse,
            Action = NetworkMouseAction.Move,
            X = x,
            Y = y
        });
    }

    private void ScreenImage_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!_inputEnabled) return;
        var (x, y) = GetScaledPos(e);
        NetworkMouseAction action = e.ChangedButton == MouseButton.Left ? NetworkMouseAction.LeftDown : NetworkMouseAction.RightDown;
        _clientService.SendInput(new ControlPacket
        {
            Type = PacketType.Mouse,
            Action = action,
            X = x,
            Y = y
        });
    }

    private void ScreenImage_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_inputEnabled) return;
        var (x, y) = GetScaledPos(e);
        NetworkMouseAction action = e.ChangedButton == MouseButton.Left ? NetworkMouseAction.LeftUp : NetworkMouseAction.RightUp;
        _clientService.SendInput(new ControlPacket
        {
            Type = PacketType.Mouse,
            Action = action,
            X = x,
            Y = y
        });
    }

    private void ScreenImage_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!_inputEnabled) return;
        _clientService.SendInput(new ControlPacket
        {
            Type = PacketType.Mouse,
            Action = NetworkMouseAction.Wheel,
            Data = e.Delta
        });
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F3)
        {
            TimingGraph.Visibility = TimingGraph.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
            return;
        }

        if (!_inputEnabled) return;
        int vk = KeyInterop.VirtualKeyFromKey(e.Key);
        _clientService.SendInput(new ControlPacket
        {
            Type = PacketType.Keyboard,
            Data = vk,
            X = 0 // Down
        });
    }

    private void Window_KeyUp(object sender, KeyEventArgs e)
    {
        if (!_inputEnabled) return;
        int vk = KeyInterop.VirtualKeyFromKey(e.Key);
        _clientService.SendInput(new ControlPacket
        {
            Type = PacketType.Keyboard,
            Data = vk,
            X = 1 // Up
        });
    }
    private DateTime _lastResize = DateTime.MinValue;

    private async void RemoteViewWindow_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Debounce simple implementation check
        if ((DateTime.Now - _lastResize).TotalMilliseconds < 200) return;
        _lastResize = DateTime.Now;

        if (_clientService != null) // Ensure connected
        {
            // Send new resolution request
            // Use ScreenImage actual size or Window Size? user said "client window size".
            // ActualWidth might be better as it excludes borders if any, but Window Size is safer for "max bounds".
            // Let's use ActualWidth of the Image container if possible, or just the window client area.
            // Grid is the content.
            var width = (int)ActualWidth;
            var height = (int)ActualHeight;

            try
            {
                _clientService.SendInput(new ControlPacket
                {
                    Type = PacketType.Settings,
                    X = width,
                    Y = height
                });
            }
            catch { }
        }
    }
}
