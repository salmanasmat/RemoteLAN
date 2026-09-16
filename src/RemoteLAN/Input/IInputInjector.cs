using RemoteLAN.Protocol.Messages;

namespace RemoteLAN.Input;

/// <summary>
/// Defines the contract for injecting keyboard and mouse input into the operating system
/// and managing input session lifecycle state.
/// </summary>
public interface IInputInjector : IDisposable
{
    /// <summary>
    /// Injects a mouse move event using normalized coordinates [0.0, 1.0].
    /// </summary>
    void InjectMouseMove(double normalizedX, double normalizedY);

    /// <summary>
    /// Injects a mouse button press or release event.
    /// </summary>
    void InjectMouseButton(MouseButtonType button, MouseButtonAction action);

    /// <summary>
    /// Injects a mouse wheel scroll event with the specified delta.
    /// </summary>
    void InjectMouseWheel(int delta);

    /// <summary>
    /// Injects a keyboard key press or release event with virtual key code and extended key flag.
    /// </summary>
    void InjectKeyboardKey(int virtualKeyCode, KeyAction action, bool isExtended);

    /// <summary>
    /// Clears any pending queued input actions, releases all currently pressed keys and mouse buttons,
    /// and associates future input with the specified session identifier.
    /// </summary>
    void ResetSession(int sessionId = 0);
}
