using System.Security.Cryptography;

namespace RemoteLAN.Security;

public sealed class PinManager : IDisposable
{
    private const string AlphanumericChars = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ";

    private string _currentPin = string.Empty;
    private bool _unattendedAccessEnabled;
    private string? _unattendedPassword;
    private int _rotationIntervalMinutes;
    private Timer? _rotationTimer;
    private readonly object _lock = new();

    public event Action<string>? PinChanged;
    public event Action<bool, string?>? UnattendedAccessChanged;

    public int RotationIntervalMinutes
    {
        get
        {
            lock (_lock)
            {
                return _rotationIntervalMinutes;
            }
        }
    }

    public string CurrentPin
    {
        get
        {
            lock (_lock)
            {
                return _currentPin;
            }
        }
    }

    public bool UnattendedAccessEnabled
    {
        get
        {
            lock (_lock)
            {
                return _unattendedAccessEnabled;
            }
        }
        set
        {
            lock (_lock)
            {
                _unattendedAccessEnabled = value;
            }
            UnattendedAccessChanged?.Invoke(value, _unattendedPassword);
        }
    }

    public string? UnattendedPassword
    {
        get
        {
            lock (_lock)
            {
                return _unattendedPassword;
            }
        }
        set
        {
            string? trimmed = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            lock (_lock)
            {
                _unattendedPassword = trimmed;
            }
            UnattendedAccessChanged?.Invoke(_unattendedAccessEnabled, trimmed);
        }
    }

    public PinManager(string? initialPin = null, bool unattendedAccessEnabled = false, string? unattendedPassword = null)
    {
        _unattendedAccessEnabled = unattendedAccessEnabled;
        _unattendedPassword = string.IsNullOrWhiteSpace(unattendedPassword) ? null : unattendedPassword.Trim();

        if (!string.IsNullOrWhiteSpace(initialPin))
        {
            SetPin(initialPin);
        }
        else
        {
            RegeneratePin();
        }
    }

    public void ConfigureUnattendedAccess(bool enabled, string? password)
    {
        string? trimmed = string.IsNullOrWhiteSpace(password) ? null : password.Trim();
        lock (_lock)
        {
            _unattendedAccessEnabled = enabled;
            if (trimmed != null)
            {
                _unattendedPassword = trimmed;
            }
        }
        UnattendedAccessChanged?.Invoke(enabled, _unattendedPassword);
    }

    public void RegeneratePin(int length = 6)
    {
        var chars = new char[length];
        for (int i = 0; i < length; i++)
        {
            chars[i] = AlphanumericChars[RandomNumberGenerator.GetInt32(AlphanumericChars.Length)];
        }
        SetPin(new string(chars));
    }

    public void SetPin(string newPin)
    {
        string trimmed = newPin?.Trim() ?? string.Empty;
        lock (_lock)
        {
            _currentPin = trimmed;
        }
        PinChanged?.Invoke(trimmed);
    }

    public bool ValidatePin(string candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate)) return false;

        string trimmed = candidate.Trim();

        lock (_lock)
        {
            // 1. Match current session access code (case-insensitive for convenience)
            if (FixedTimeEquals(_currentPin, trimmed, ignoreCase: true))
            {
                return true;
            }

            // 2. Match unattended access password if enabled (case-sensitive)
            if (_unattendedAccessEnabled && !string.IsNullOrWhiteSpace(_unattendedPassword))
            {
                if (FixedTimeEquals(_unattendedPassword, trimmed, ignoreCase: false))
                {
                    return true;
                }
            }

            return false;
        }
    }

    private static bool FixedTimeEquals(string? a, string? b, bool ignoreCase)
    {
        if (a == null || b == null) return false;
        if (ignoreCase)
        {
            a = a.ToUpperInvariant();
            b = b.ToUpperInvariant();
        }

        byte[] aBytes = System.Text.Encoding.UTF8.GetBytes(a);
        byte[] bBytes = System.Text.Encoding.UTF8.GetBytes(b);

        return CryptographicOperations.FixedTimeEquals(aBytes, bBytes);
    }

    public void SetRotationInterval(int minutes)
    {
        lock (_lock)
        {
            _rotationIntervalMinutes = Math.Max(0, minutes);
            _rotationTimer?.Dispose();
            _rotationTimer = null;

            if (_rotationIntervalMinutes > 0)
            {
                var interval = TimeSpan.FromMinutes(_rotationIntervalMinutes);
                _rotationTimer = new Timer(OnRotationTimerElapsed, null, interval, interval);
            }
        }
    }

    internal void OnRotationTimerElapsed(object? state = null)
    {
        RegeneratePin();
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _rotationTimer?.Dispose();
            _rotationTimer = null;
        }
    }
}
