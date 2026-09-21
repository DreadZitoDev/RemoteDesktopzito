namespace RemoteClanker.Shared;

public enum MessageType : byte
{
    Challenge = 1,
    AuthResponse = 2,
    AuthResult = 3,
    ScreenInfo = 10,
    FrameUpdate = 11,
    MouseMove = 20,
    MouseButton = 21,
    MouseWheel = 22,
    Key = 30,
}

public enum MouseButton : byte
{
    Left,
    Right,
    Middle,
    X1,
    X2,
}

[Flags]
public enum KeyFlags : byte
{
    None = 0,
    Extended = 1,
    Up = 2,
}

public static class Protocol
{
    public const int DEFAULT_PORT = 45900;
    public const int MAX_FRAME_PAYLOAD = 32 * 1024 * 1024;
    public const int MAX_AUTH_PAYLOAD = 256;
    public const int MAX_INPUT_PAYLOAD = 64;
}
