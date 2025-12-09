namespace Remotier.Services.Network;

public enum PacketType : byte
{
    Frame = 0x01,
    Mouse = 0x02,
    Keyboard = 0x03,
    Connect = 0x04,
    Disconnect = 0x05,
    Settings = 0x06,
    Chat = 0x07
}

public enum MouseAction : byte
{
    Move = 0x01,
    LeftDown = 0x02,
    LeftUp = 0x03,
    RightDown = 0x04,
    RightUp = 0x05,
    Wheel = 0x06
}
