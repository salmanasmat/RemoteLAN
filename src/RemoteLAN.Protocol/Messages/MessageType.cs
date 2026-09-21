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
    KeyboardKey = 0x30,

    // Lock Screen & System Control
    SendCtrlAltDel = 0x35,
    PowerAction = 0x36,
    UnlockWithOsPassword = 0x37,

    // Phase 4 — In-Session Ephemeral Chat
    ChatMessage = 0x40,
    ChatTypingIndicator = 0x41
}
