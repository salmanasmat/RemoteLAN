using System.Security.Cryptography;

namespace RemoteLAN.Agent.Security;

public sealed class PinManager
{
    private string _currentPin = string.Empty;
    private readonly object _lock = new();

    public event Action<string>? PinChanged;

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

    public PinManager(string? initialPin = null)
    {
        if (!string.IsNullOrWhiteSpace(initialPin))
        {
            SetPin(initialPin);
        }
        else
        {
            RegeneratePin();
        }
    }

    public void RegeneratePin()
    {
        // Generate random 6-digit PIN securely
        int pinNumber = RandomNumberGenerator.GetInt32(100000, 1000000);
        SetPin(pinNumber.ToString());
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

        lock (_lock)
        {
            return string.Equals(_currentPin, candidate.Trim(), StringComparison.Ordinal);
        }
    }
}
