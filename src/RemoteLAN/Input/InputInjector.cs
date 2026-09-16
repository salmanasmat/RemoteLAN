using System.Diagnostics;
using System.Runtime.InteropServices;
using RemoteLAN.Protocol.Messages;
using RemoteLAN.Security;

namespace RemoteLAN.Input;

public sealed class InputInjector
{
    private readonly Stopwatch _desktopCheckStopwatch = Stopwatch.StartNew();

    private void EnsureInputDesktop(bool force = false)
    {
        if (force || _desktopCheckStopwatch.ElapsedMilliseconds >= 200)
        {
            _desktopCheckStopwatch.Restart();
            DesktopManager.EnsureThreadOnInputDesktop(out _);
        }
    }

    public void InjectMouseMove(double normalizedX, double normalizedY)
    {
        EnsureInputDesktop(false);
        int absX = (int)Math.Round(Math.Clamp(normalizedX, 0.0, 1.0) * 65535.0);
        int absY = (int)Math.Round(Math.Clamp(normalizedY, 0.0, 1.0) * 65535.0);

        var input = new NativeMethods.INPUT
        {
            type = NativeMethods.INPUT_MOUSE,
            u = new NativeMethods.InputUnion
            {
                mi = new NativeMethods.MOUSEINPUT
                {
                    dx = absX,
                    dy = absY,
                    mouseData = 0,
                    dwFlags = NativeMethods.MOUSEEVENTF_MOVE | NativeMethods.MOUSEEVENTF_ABSOLUTE | NativeMethods.MOUSEEVENTF_VIRTUALDESK,
                    time = 0,
                    dwExtraInfo = UIntPtr.Zero
                }
            }
        };

        NativeMethods.SendInput(1, new[] { input }, Marshal.SizeOf<NativeMethods.INPUT>());
    }

    public void InjectMouseButton(MouseButtonType button, MouseButtonAction action)
    {
        EnsureInputDesktop(true);
        uint flags = button switch
        {
            MouseButtonType.Left => action == MouseButtonAction.Down ? NativeMethods.MOUSEEVENTF_LEFTDOWN : NativeMethods.MOUSEEVENTF_LEFTUP,
            MouseButtonType.Right => action == MouseButtonAction.Down ? NativeMethods.MOUSEEVENTF_RIGHTDOWN : NativeMethods.MOUSEEVENTF_RIGHTUP,
            MouseButtonType.Middle => action == MouseButtonAction.Down ? NativeMethods.MOUSEEVENTF_MIDDLEDOWN : NativeMethods.MOUSEEVENTF_MIDDLEUP,
            _ => 0
        };

        if (flags == 0) return;

        var input = new NativeMethods.INPUT
        {
            type = NativeMethods.INPUT_MOUSE,
            u = new NativeMethods.InputUnion
            {
                mi = new NativeMethods.MOUSEINPUT
                {
                    dx = 0,
                    dy = 0,
                    mouseData = 0,
                    dwFlags = flags,
                    time = 0,
                    dwExtraInfo = UIntPtr.Zero
                }
            }
        };

        NativeMethods.SendInput(1, new[] { input }, Marshal.SizeOf<NativeMethods.INPUT>());
    }

    public void InjectMouseWheel(int delta)
    {
        var input = new NativeMethods.INPUT
        {
            type = NativeMethods.INPUT_MOUSE,
            u = new NativeMethods.InputUnion
            {
                mi = new NativeMethods.MOUSEINPUT
                {
                    dx = 0,
                    dy = 0,
                    mouseData = unchecked((uint)delta),
                    dwFlags = NativeMethods.MOUSEEVENTF_WHEEL,
                    time = 0,
                    dwExtraInfo = UIntPtr.Zero
                }
            }
        };

        NativeMethods.SendInput(1, new[] { input }, Marshal.SizeOf<NativeMethods.INPUT>());
    }

    public void InjectKeyboardKey(int virtualKeyCode, KeyAction action, bool isExtended)
    {
        EnsureInputDesktop(true);
        uint flags = 0;
        if (action == KeyAction.Up) flags |= NativeMethods.KEYEVENTF_KEYUP;
        if (isExtended) flags |= NativeMethods.KEYEVENTF_EXTENDEDKEY;

        var input = new NativeMethods.INPUT
        {
            type = NativeMethods.INPUT_KEYBOARD,
            u = new NativeMethods.InputUnion
            {
                ki = new NativeMethods.KEYBDINPUT
                {
                    wVk = (ushort)virtualKeyCode,
                    wScan = 0,
                    dwFlags = flags,
                    time = 0,
                    dwExtraInfo = UIntPtr.Zero
                }
            }
        };

        NativeMethods.SendInput(1, new[] { input }, Marshal.SizeOf<NativeMethods.INPUT>());
    }
}
