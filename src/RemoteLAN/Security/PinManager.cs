using System.Security.Cryptography;

namespace RemoteLAN.Security;

public sealed class PinManager
{
    private const string AlphanumericChars = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ";

    private string _currentPin = string.Empty;
    private bool _unattendedAccessEnabled;
    private string? _unattendedPassword;
    private readonly object _lock = new();

    public event Action<string>? PinChanged;
    public event Action<bool, string?>? UnattendedAccessChanged;

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
            if (string.Equals(_currentPin, trimmed, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // 2. Match unattended access password if enabled (case-sensitive)
            if (_unattendedAccessEnabled && !string.IsNullOrWhiteSpace(_unattendedPassword))
            {
                if (string.Equals(_unattendedPassword, trimmed, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
