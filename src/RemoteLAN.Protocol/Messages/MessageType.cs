namespace RemoteLAN.Protocol.Messages;

public enum MessageType : byte
{
    None = 0x00,
    
    // Phase 3 - Auth
    AuthRequest = 0x01,
    AuthResponse = 0x02,

    // Phase 1 - Screen Streaming
    ScreenFrame = 0x10,

    // Phase 2 - Input Injection
    MouseMove = 0x20,
    MouseButton = 0x21,
    MouseWheel = 0x22,
    KeyboardKey = 0x30
}
