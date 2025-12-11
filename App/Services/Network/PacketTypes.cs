namespace Remotier.Services.Network;

public enum PacketType : byte
{
    Frame = 0x01,
    Mouse = 0x02,
    Keyboard = 0x03,
    Connect = 0x04,
    Disconnect = 0x05,
    Settings = 0x06,
    Chat = 0x07,
    Clipboard = 0x08,
    FileStart = 0x09,
    FileChunk = 0x0A,
    FileEnd = 0x0B,
    AuthChallenge = 0x0C,
    AuthResponse = 0x0D,
    AuthResult = 0x0E
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

[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = 1)]
public struct AuthChallengePacket
{
    public PacketType Type;
    [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValTStr, SizeConst = 64)]
    public string Salt; // Random salt
}

[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = 1)]
public struct AuthResponsePacket
{
    public PacketType Type;
    [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValTStr, SizeConst = 128)]
    public string DeviceId;
    [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValTStr, SizeConst = 128)]
    public string DeviceName; // For display
    [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValTStr, SizeConst = 128)]
    public string AccountName;
    [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValTStr, SizeConst = 128)]
    public string PasswordHash; // Hash(Start + Hash(Pass) + Salt)
}

[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = 1)]
public struct AuthResultPacket
{
    public PacketType Type;
    public bool IsAuthenticated;
    public bool IsTrusted;
}
