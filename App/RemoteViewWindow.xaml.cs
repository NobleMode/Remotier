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

    public RemoteViewWindow(ConnectionInfo info)
    {
        InitializeComponent();
        _info = info;
        Loaded += RemoteViewWindow_Loaded;
        Closing += RemoteViewWindow_Closing;
    }

    private int _frameCount = 0;
    private DateTime _lastUpdate = DateTime.Now;

    private async void RemoteViewWindow_Loaded(object sender, RoutedEventArgs e)
    {
        DebugInfo.Text = $"Connecting to {_info.IP}:{_info.Port}...";
        _clientService = new ClientService();

        _clientService.OnFrameReady += (img) => Dispatcher.Invoke(() =>
        {
            ScreenImage.Source = img;
            _frameCount++;
            if ((DateTime.Now - _lastUpdate).TotalSeconds >= 1)
            {
                DebugInfo.Text = $"Connected to {_info.IP}\nFPS: {_frameCount}\nResolution: {img.PixelWidth}x{img.PixelHeight}";
                _frameCount = 0;
                _lastUpdate = DateTime.Now;
            }
        });

        try
        {
            await _clientService.ConnectAsync(_info.IP, _info.Port);
            DebugInfo.Text = $"Handshake Sent. Waiting for video...";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Connection failed: {ex.Message}\nCheck IP and Firewall on Host.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            Close();
        }
    }

    private void RemoteViewWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        _clientService?.Dispose();
    }

    // Input Handling

    // Helper to scale coordinates if image is stretched
    private (int x, int y) GetScaledPos(MouseEventArgs e)
    {
        var pos = e.GetPosition(ScreenImage);
        var actualWidth = ScreenImage.ActualWidth;
        var actualHeight = ScreenImage.ActualHeight;

        // Caution: If Image Source is null, we can't calculate scaling
        if (ScreenImage.Source == null) return ((int)pos.X, (int)pos.Y);

        var naturalWidth = ScreenImage.Source.Width;
        var naturalHeight = ScreenImage.Source.Height;

        // Assuming Stretch="Uniform", we need to account for letterboxing if any.
        // For MVP, lets assume simple mapping or that Image fills the view (Stretch="Fill" might be easier for logic but looks bad).
        // Let's rely on relative position 0-1 and Host scales it back? No, Host InputService expects absolute px.

        // NOTE: InputService SetCursorPos expects Screen Coordinates.

        // TODO: Ideally send normalized 0.0-1.0 coords and Host multiplies by its Screen Resolution.
        // But HostService.InputService uses SetCursorPos(x, y). 
        // Let's send normalized coords in Packet X,Y as 0-65535 (standard Windows mouse_event absolute range).

        double normX = pos.X / actualWidth;
        double normY = pos.Y / actualHeight;

        // Win32 Absolute units are 0 to 65535
        int absX = (int)(normX * 65535);
        int absY = (int)(normY * 65535);

        // HACK: Host InputService as currently written uses SetCursorPos(X,Y) which expects PIXELS.
        // We need updates in InputService to support MOUSEEVENTF_ABSOLUTE (0x8000).
        // For now, let's keep sending pixels assuming 1:1 or let Host handle it.
        // But we don't know Host Resolution here!
        // To make it robust: Send Normalized (0-65535) and update InputService to use MOUSEEVENTF_ABSOLUTE.

        return (absX, absY);
    }

    private void ScreenImage_MouseMove(object sender, MouseEventArgs e)
    {
        // Rate limit?
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
        _clientService.SendInput(new ControlPacket
        {
            Type = PacketType.Mouse,
            Action = NetworkMouseAction.Wheel,
            Data = e.Delta
        });
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
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
        int vk = KeyInterop.VirtualKeyFromKey(e.Key);
        _clientService.SendInput(new ControlPacket
        {
            Type = PacketType.Keyboard,
            Data = vk,
            X = 1 // Up
        });
    }
}
