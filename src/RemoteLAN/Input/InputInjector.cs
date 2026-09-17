using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using RemoteLAN.Protocol.Messages;
using RemoteLAN.Security;

namespace RemoteLAN.Input;

public sealed class InputInjector : IInputInjector
{
    private readonly struct QueueItem
    {
        public readonly int SessionId;
        public readonly Action Action;

        public QueueItem(int sessionId, Action action)
        {
            SessionId = sessionId;
            Action = action;
        }
    }

    private readonly BlockingCollection<QueueItem> _queue = new();
    private readonly Thread _workerThread;
    private readonly CancellationTokenSource _cts = new();
    private readonly Stopwatch _desktopCheckStopwatch = Stopwatch.StartNew();

    // Thread-safe state tracking collections
    private readonly ConcurrentDictionary<int, bool> _pressedKeys = new();
    private readonly ConcurrentDictionary<MouseButtonType, bool> _pressedButtons = new();
    private readonly Func<NativeMethods.INPUT[], uint>? _sendInputOverride;
    private volatile int _currentSessionId;
    private bool _disposed;

    public InputInjector() : this(null)
    {
    }

    internal InputInjector(Func<NativeMethods.INPUT[], uint>? sendInputOverride)
    {
        _sendInputOverride = sendInputOverride;
        _workerThread = new Thread(ProcessQueueLoop)
        {
            IsBackground = true,
            Name = "RemoteLAN_InputInjectorWorker"
        };
        _workerThread.Start();
    }

    private uint PerformSendInput(NativeMethods.INPUT[] inputs)
    {
        if (_sendInputOverride != null)
        {
            return _sendInputOverride(inputs);
        }
        return NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
    }

    private void ProcessQueueLoop()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                if (_queue.TryTake(out var item, 100, _cts.Token))
                {
                    // Discard stale events if session ID has changed
                    if (item.SessionId == _currentSessionId)
                    {
                        item.Action();
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[InputInjector] Worker error: {ex.Message}");
            }
        }
    }

    private void EnsureInputDesktop(bool force = false)
    {
        if (_sendInputOverride != null) return;
        if (force || _desktopCheckStopwatch.ElapsedMilliseconds >= 200)
        {
            _desktopCheckStopwatch.Restart();
            DesktopManager.EnsureThreadOnInputDesktop(out _);
        }
    }

    public void InjectMouseMove(double normalizedX, double normalizedY)
    {
        if (_disposed) return;

        int sessionId = _currentSessionId;
        _queue.Add(new QueueItem(sessionId, () =>
        {
            EnsureInputDesktop(false);
            int absX = (int)Math.Round(Math.Clamp(normalizedX, 0.0, 1.0) * 65535.0);
            int absY = (int)Math.Round(Math.Clamp(normalizedY, 0.0, 1.0) * 65535.0);

            // Note: Use primary display absolute mapping (without MOUSEEVENTF_VIRTUALDESK)
            // because ScreenCapturer captures the primary monitor geometry.
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
                        dwFlags = NativeMethods.MOUSEEVENTF_MOVE | NativeMethods.MOUSEEVENTF_ABSOLUTE,
                        time = 0,
                        dwExtraInfo = UIntPtr.Zero
                    }
                }
            };

            IDisposable? scope = DesktopManager.IsLockScreenActiveCached && DesktopManager.IsAdministrator
                ? DesktopManager.ImpersonateSystemScope()
                : null;

            try
            {
                uint sent = PerformSendInput(new[] { input });
                if (sent == 0)
                {
                    Debug.WriteLine($"[InputInjector] SendInput mouse move failed: {Marshal.GetLastWin32Error()}");
                }
            }
            finally
            {
                scope?.Dispose();
            }
        }));
    }

    public void InjectMouseButton(MouseButtonType button, MouseButtonAction action)
    {
        if (_disposed) return;

        if (action == MouseButtonAction.Down)
        {
            _pressedButtons.TryAdd(button, true);
        }
        else
        {
            _pressedButtons.TryRemove(button, out _);
        }

        int sessionId = _currentSessionId;
        _queue.Add(new QueueItem(sessionId, () =>
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

            IDisposable? scope = DesktopManager.IsLockScreenActiveCached && DesktopManager.IsAdministrator
                ? DesktopManager.ImpersonateSystemScope()
                : null;

            try
            {
                uint sent = PerformSendInput(new[] { input });
                if (sent == 0)
                {
                    Debug.WriteLine($"[InputInjector] SendInput mouse button failed: {Marshal.GetLastWin32Error()}");
                }
            }
            finally
            {
                scope?.Dispose();
            }
        }));
    }

    public void InjectMouseWheel(int delta)
    {
        if (_disposed) return;

        int sessionId = _currentSessionId;
        _queue.Add(new QueueItem(sessionId, () =>
        {
            EnsureInputDesktop(false);
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

            IDisposable? scope = DesktopManager.IsLockScreenActiveCached && DesktopManager.IsAdministrator
                ? DesktopManager.ImpersonateSystemScope()
                : null;

            try
            {
                uint sent = PerformSendInput(new[] { input });
                if (sent == 0)
                {
                    Debug.WriteLine($"[InputInjector] SendInput mouse wheel failed: {Marshal.GetLastWin32Error()}");
                }
            }
            finally
            {
                scope?.Dispose();
            }
        }));
    }

    public void InjectKeyboardKey(int virtualKeyCode, KeyAction action, bool isExtended)
    {
        if (_disposed || virtualKeyCode <= 0) return;

        if (action == KeyAction.Down)
        {
            _pressedKeys.TryAdd(virtualKeyCode, true);
        }
        else
        {
            _pressedKeys.TryRemove(virtualKeyCode, out _);
        }

        int sessionId = _currentSessionId;
        _queue.Add(new QueueItem(sessionId, () =>
        {
            EnsureInputDesktop(true);

            // Translate Virtual Key to physical hardware scan code
            ushort scanCode = (ushort)NativeMethods.MapVirtualKey((uint)virtualKeyCode, NativeMethods.MAPVK_VK_TO_VSC);

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
                        wScan = scanCode,
                        dwFlags = flags,
                        time = 0,
                        dwExtraInfo = UIntPtr.Zero
                    }
                }
            };

            IDisposable? scope = DesktopManager.IsLockScreenActiveCached && DesktopManager.IsAdministrator
                ? DesktopManager.ImpersonateSystemScope()
                : null;

            try
            {
                uint sent = PerformSendInput(new[] { input });
                if (sent == 0)
                {
                    int err = Marshal.GetLastWin32Error();
                    Debug.WriteLine($"[InputInjector] SendInput keyboard VK={virtualKeyCode} scan={scanCode} failed: {err}");
                }
            }
            finally
            {
                scope?.Dispose();
            }
        }));
    }

    /// <summary>
    /// Clears any pending queued input actions, immediately releases any logically pressed keys
    /// and mouse buttons, and associates future inputs with the new session identifier.
    /// </summary>
    public void ResetSession(int sessionId = 0)
    {
        if (_disposed) return;

        // 1. Advance session ID so any in-flight dequeued items from the previous session are discarded
        _currentSessionId = sessionId;

        // 2. Clear any pending queued items
        while (_queue.TryTake(out _)) { }

        // 3. Release any keys that remain logically pressed
        if (!_pressedKeys.IsEmpty)
        {
            var keys = _pressedKeys.Keys.ToArray();
            _pressedKeys.Clear();

            foreach (int vk in keys)
            {
                EnsureInputDesktop(true);
                ushort scanCode = (ushort)NativeMethods.MapVirtualKey((uint)vk, NativeMethods.MAPVK_VK_TO_VSC);
                var input = new NativeMethods.INPUT
                {
                    type = NativeMethods.INPUT_KEYBOARD,
                    u = new NativeMethods.InputUnion
                    {
                        ki = new NativeMethods.KEYBDINPUT
                        {
                            wVk = (ushort)vk,
                            wScan = scanCode,
                            dwFlags = NativeMethods.KEYEVENTF_KEYUP,
                            time = 0,
                            dwExtraInfo = UIntPtr.Zero
                        }
                    }
                };

                IDisposable? scope = DesktopManager.IsLockScreenActive() && DesktopManager.IsAdministrator
                    ? DesktopManager.ImpersonateSystemScope()
                    : null;
                try
                {
                    PerformSendInput(new[] { input });
                }
                catch { }
                finally
                {
                    scope?.Dispose();
                }
            }
        }

        // 4. Release any mouse buttons that remain logically pressed
        if (!_pressedButtons.IsEmpty)
        {
            var buttons = _pressedButtons.Keys.ToArray();
            _pressedButtons.Clear();

            foreach (var btn in buttons)
            {
                uint flag = btn switch
                {
                    MouseButtonType.Left => NativeMethods.MOUSEEVENTF_LEFTUP,
                    MouseButtonType.Right => NativeMethods.MOUSEEVENTF_RIGHTUP,
                    MouseButtonType.Middle => NativeMethods.MOUSEEVENTF_MIDDLEUP,
                    _ => 0
                };

                if (flag == 0) continue;

                EnsureInputDesktop(true);
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
                            dwFlags = flag,
                            time = 0,
                            dwExtraInfo = UIntPtr.Zero
                        }
                    }
                };

                IDisposable? scope = DesktopManager.IsLockScreenActive() && DesktopManager.IsAdministrator
                    ? DesktopManager.ImpersonateSystemScope()
                    : null;
                try
                {
                    PerformSendInput(new[] { input });
                }
                catch { }
                finally
                {
                    scope?.Dispose();
                }
            }
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            ResetSession(0);
            _cts.Cancel();
            _queue.CompleteAdding();
            try
            {
                if (!_workerThread.Join(500))
                {
                    Debug.WriteLine("[InputInjector] Worker thread join timed out");
                }
            }
            catch { }
            _queue.Dispose();
            _cts.Dispose();
        }
    }
}
