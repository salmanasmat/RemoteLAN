using System.Diagnostics;
using System.Text.Json;
using RemoteLAN.Input;
using RemoteLAN.Protocol.Messages;
using RemoteLAN.Security;

namespace RemoteLAN.WebBridge;

public sealed class WebBridgeInputHandler
{
    private readonly IInputInjector _inputInjector;

    public WebBridgeInputHandler(IInputInjector inputInjector)
    {
        _inputInjector = inputInjector ?? throw new ArgumentNullException(nameof(inputInjector));
    }

    public void HandleInputMessage(JsonElement root)
    {
        if (!root.TryGetProperty("type", out var typeProp)) return;

        string? type = typeProp.GetString();
        switch (type)
        {
            case "mouse":
                HandleMouse(root);
                break;
            case "scroll":
                HandleScroll(root);
                break;
            case "key":
                HandleKey(root);
                break;
            case "special":
                HandleSpecial(root);
                break;
        }
    }

    private void HandleMouse(JsonElement root)
    {
        string? action = root.TryGetProperty("action", out var actionProp) ? actionProp.GetString() : null;
        double x = root.TryGetProperty("x", out var xProp) ? xProp.GetDouble() : 0.0;
        double y = root.TryGetProperty("y", out var yProp) ? yProp.GetDouble() : 0.0;

        if (action == "move")
        {
            _inputInjector.InjectMouseMove(x, y);
            return;
        }

        string? buttonStr = root.TryGetProperty("button", out var btnProp) ? btnProp.GetString() : "left";
        var button = buttonStr switch
        {
            "right" => MouseButtonType.Right,
            "middle" => MouseButtonType.Middle,
            _ => MouseButtonType.Left
        };

        var mouseAction = action == "down" ? MouseButtonAction.Down : MouseButtonAction.Up;

        // If coordinates provided with click, move cursor first
        if (root.TryGetProperty("x", out _) && root.TryGetProperty("y", out _))
        {
            _inputInjector.InjectMouseMove(x, y);
        }

        _inputInjector.InjectMouseButton(button, mouseAction);
    }

    private void HandleScroll(JsonElement root)
    {
        if (root.TryGetProperty("deltaY", out var deltaProp))
        {
            int delta = deltaProp.GetInt32();
            _inputInjector.InjectMouseWheel(delta);
        }
    }

    private void HandleKey(JsonElement root)
    {
        string? actionStr = root.TryGetProperty("action", out var aProp) ? aProp.GetString() : null;

        if (actionStr == "char" && root.TryGetProperty("char", out var charProp))
        {
            string? chStr = charProp.GetString();
            if (!string.IsNullOrEmpty(chStr))
            {
                foreach (char c in chStr)
                {
                    DesktopManager.SendUnicodeCharDirect(c);
                }
            }
            return;
        }

        var action = actionStr == "up" ? KeyAction.Up : KeyAction.Down;

        int vk = 0;
        if (root.TryGetProperty("code", out var codeProp))
        {
            vk = MapJsKeyCodeToVirtualKey(codeProp.GetInt32());
        }

        if (vk == 0 && root.TryGetProperty("key", out var keyProp))
        {
            vk = MapKeyStringToVirtualKey(keyProp.GetString());
        }

        if (vk > 0)
        {
            bool isExtended = IsExtendedKey(vk);
            _inputInjector.InjectKeyboardKey(vk, action, isExtended);
        }
    }

    private void HandleSpecial(JsonElement root)
    {
        string? command = root.TryGetProperty("command", out var cmdProp) ? cmdProp.GetString() : null;

        switch (command)
        {
            case "ctrl_alt_del":
                DesktopManager.SendCtrlAltDel();
                break;

            case "unlock":
                if (root.TryGetProperty("password", out var pwdProp))
                {
                    string? password = pwdProp.GetString();
                    if (!string.IsNullOrEmpty(password))
                    {
                        DesktopManager.UnlockWithPassword(password);
                    }
                }
                break;

            case "key_press":
                if (root.TryGetProperty("key", out var keyProp))
                {
                    int vk = MapKeyStringToVirtualKey(keyProp.GetString());
                    if (vk > 0)
                    {
                        bool isExtended = IsExtendedKey(vk);
                        _inputInjector.InjectKeyboardKey(vk, KeyAction.Down, isExtended);
                        Thread.Sleep(20);
                        _inputInjector.InjectKeyboardKey(vk, KeyAction.Up, isExtended);
                    }
                }
                break;
        }
    }

    private static int MapJsKeyCodeToVirtualKey(int jsCode)
    {
        return jsCode switch
        {
            8 => 0x08,   // Backspace
            9 => 0x09,   // Tab
            13 => 0x0D,  // Enter
            16 => 0x10,  // Shift
            17 => 0x11,  // Control
            18 => 0x12,  // Alt
            27 => 0x1B,  // Escape
            32 => 0x20,  // Space
            37 => 0x25,  // Left arrow
            38 => 0x26,  // Up arrow
            39 => 0x27,  // Right arrow
            40 => 0x28,  // Down arrow
            46 => 0x2E,  // Delete
            91 or 92 or 93 => 0x5B, // Windows key / Meta
            >= 48 and <= 57 => jsCode, // 0-9
            >= 65 and <= 90 => jsCode, // A-Z
            _ => jsCode
        };
    }

    private static int MapKeyStringToVirtualKey(string? key)
    {
        if (string.IsNullOrEmpty(key)) return 0;

        return key.ToLowerInvariant() switch
        {
            "enter" => 0x0D,
            "escape" or "esc" => 0x1B,
            "tab" => 0x09,
            "backspace" => 0x08,
            "space" or " " => 0x20,
            "meta" or "win" or "os" => 0x5B,
            "arrowup" or "up" => 0x26,
            "arrowdown" or "down" => 0x28,
            "arrowleft" or "left" => 0x25,
            "arrowright" or "right" => 0x27,
            "delete" or "del" => 0x2E,
            _ when key.Length == 1 && char.IsLetterOrDigit(key[0]) => (int)char.ToUpperInvariant(key[0]),
            _ => 0
        };
    }

    private static bool IsExtendedKey(int vk)
    {
        return vk switch
        {
            0x25 or 0x26 or 0x27 or 0x28 => true, // Arrows
            0x2D or 0x2E => true,                 // Insert, Delete
            0x24 or 0x23 => true,                 // Home, End
            0x21 or 0x22 => true,                 // PageUp, PageDown
            0x5B or 0x5C => true,                 // Windows keys
            _ => false
        };
    }
}
