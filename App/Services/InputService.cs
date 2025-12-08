using System.Runtime.InteropServices;
using Remotier.Services.Network;

namespace Remotier.Services;

public class InputService
{
    [DllImport("user32.dll")]
    private static extern void mouse_event(uint dwFlags, int dx, int dy, int dwData, int dwExtraInfo);

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int X, int Y);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, int dwExtraInfo);

    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    private const uint MOUSEEVENTF_WHEEL = 0x0800;

    private const uint KEYEVENTF_KEYUP = 0x0002;
    // EXTENDEDKEY is 0x0001, skipping for now

    public void HandleInput(ControlPacket packet)
    {
        switch (packet.Type)
        {
            case PacketType.Mouse:
                HandleMouse(packet);
                break;
            case PacketType.Keyboard:
                HandleKeyboard(packet);
                break;
        }
    }

    private void HandleMouse(ControlPacket packet)
    {
        // For absolute movement, we might need to know screen resolution scaling.
        // Assuming packet.X, packet.Y are absolute coordinates or scaled correctly.

        switch (packet.Action)
        {
            case MouseAction.Move:
                SetCursorPos(packet.X, packet.Y);
                break;
            case MouseAction.LeftDown:
                mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0);
                break;
            case MouseAction.LeftUp:
                mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, 0);
                break;
            case MouseAction.RightDown:
                mouse_event(MOUSEEVENTF_RIGHTDOWN, 0, 0, 0, 0);
                break;
            case MouseAction.RightUp:
                mouse_event(MOUSEEVENTF_RIGHTUP, 0, 0, 0, 0);
                break;
            case MouseAction.Wheel:
                mouse_event(MOUSEEVENTF_WHEEL, 0, 0, packet.Data, 0);
                break;
        }
    }

    private void HandleKeyboard(ControlPacket packet)
    {
        // packet.Data could hold the VK code
        // packet.X could hold 1 for down, 0 for up? Or use Action if we defined KeyboardAction
        // Reusing MouseAction logic is messy. PacketType.Keyboard implies we should use specific fields.

        // Let's assume packet.Data = KeyCode, packet.X = 0 (Down) or 1 (Up)
        byte vk = (byte)packet.Data;
        bool isUp = packet.X == 1;

        uint flags = isUp ? KEYEVENTF_KEYUP : 0;
        keybd_event(vk, 0, flags, 0);
    }
}
