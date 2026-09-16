using System.Collections.Concurrent;
using RemoteLAN.Input;
using RemoteLAN.Protocol.Messages;

namespace RemoteLAN.Tests;

/// <summary>
/// Test double for IInputInjector that captures all input events in memory
/// without invoking Win32 SendInput or modifying physical desktop state.
/// </summary>
public sealed class TestInputInjector : IInputInjector
{
    public ConcurrentQueue<(double X, double Y)> MouseMoves { get; } = new();
    public ConcurrentQueue<(MouseButtonType Button, MouseButtonAction Action)> MouseButtons { get; } = new();
    public ConcurrentQueue<int> MouseWheels { get; } = new();
    public ConcurrentQueue<(int VirtualKeyCode, KeyAction Action, bool IsExtended)> Keys { get; } = new();
    public ConcurrentQueue<int> SessionResets { get; } = new();

    public event Action<(double X, double Y)>? MouseMoveInjected;
    public event Action<(MouseButtonType Button, MouseButtonAction Action)>? MouseButtonInjected;
    public event Action<int>? MouseWheelInjected;
    public event Action<(int VirtualKeyCode, KeyAction Action, bool IsExtended)>? KeyInjected;

    public void InjectMouseMove(double normalizedX, double normalizedY)
    {
        var item = (normalizedX, normalizedY);
        MouseMoves.Enqueue(item);
        MouseMoveInjected?.Invoke(item);
    }

    public void InjectMouseButton(MouseButtonType button, MouseButtonAction action)
    {
        var item = (button, action);
        MouseButtons.Enqueue(item);
        MouseButtonInjected?.Invoke(item);
    }

    public void InjectMouseWheel(int delta)
    {
        MouseWheels.Enqueue(delta);
        MouseWheelInjected?.Invoke(delta);
    }

    public void InjectKeyboardKey(int virtualKeyCode, KeyAction action, bool isExtended)
    {
        var item = (virtualKeyCode, action, isExtended);
        Keys.Enqueue(item);
        KeyInjected?.Invoke(item);
    }

    public void ResetSession(int sessionId = 0)
    {
        SessionResets.Enqueue(sessionId);
    }

    public void Dispose()
    {
    }
}
