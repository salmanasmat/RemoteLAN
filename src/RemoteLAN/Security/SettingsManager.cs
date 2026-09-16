using System.IO;
using System.Text.Json;

namespace RemoteLAN.Security;

public sealed class SettingsManager
{
    private readonly string _filePath;
    private readonly object _lock = new();

    public sealed class SettingsData
    {
        public string? HostPin { get; set; }
        public bool UnattendedAccessEnabled { get; set; }
        public string? UnattendedPassword { get; set; }
        public Dictionary<string, string> SavedPasswords { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        // Security / Unauthorized access protection
        public bool BlockUnauthorizedAttempts { get; set; } = true;
        public int MaxFailedAuthAttempts { get; set; } = 5;
        public int LockoutDurationMinutes { get; set; } = 10;
        public int PinRotationIntervalMinutes { get; set; } = 0; // 0 = Never (Default)

        // General application preferences
        public bool StartMinimizedToTray { get; set; } = false;
        public bool MinimizeToTrayOnClose { get; set; } = true;
    }

    private SettingsData _data = new();
    private readonly Dictionary<string, int> _failedAttempts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTime> _lockouts = new(StringComparer.OrdinalIgnoreCase);

    public SettingsManager(string? filePath = null)
    {
        _filePath = filePath ?? GetDefaultFilePath();
        Load();
    }

    public static string GetDefaultFilePath()
    {
        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RemoteLAN");
        return Path.Combine(dir, "settings.json");
    }

    public string? GetHostPin()
    {
        lock (_lock)
        {
            return _data.HostPin;
        }
    }

    public void SaveHostPin(string pin)
    {
        lock (_lock)
        {
            _data.HostPin = pin;
            SaveLocked();
        }
    }

    public bool IsUnattendedAccessEnabled()
    {
        lock (_lock)
        {
            return _data.UnattendedAccessEnabled;
        }
    }

    public string? GetUnattendedPassword()
    {
        lock (_lock)
        {
            return _data.UnattendedPassword;
        }
    }

    public void SetUnattendedAccess(bool enabled, string? password = null)
    {
        lock (_lock)
        {
            _data.UnattendedAccessEnabled = enabled;
            if (password != null)
            {
                _data.UnattendedPassword = password;
            }
            SaveLocked();
        }
    }

    public bool BlockUnauthorizedAttempts
    {
        get { lock (_lock) return _data.BlockUnauthorizedAttempts; }
        set { lock (_lock) { _data.BlockUnauthorizedAttempts = value; SaveLocked(); } }
    }

    public int MaxFailedAuthAttempts
    {
        get { lock (_lock) return _data.MaxFailedAuthAttempts; }
        set { lock (_lock) { _data.MaxFailedAuthAttempts = Math.Max(1, value); SaveLocked(); } }
    }

    public int LockoutDurationMinutes
    {
        get { lock (_lock) return _data.LockoutDurationMinutes; }
        set { lock (_lock) { _data.LockoutDurationMinutes = Math.Max(1, value); SaveLocked(); } }
    }

    public int PinRotationIntervalMinutes
    {
        get { lock (_lock) return _data.PinRotationIntervalMinutes; }
        set { lock (_lock) { _data.PinRotationIntervalMinutes = Math.Max(0, value); SaveLocked(); } }
    }

    public bool StartMinimizedToTray
    {
        get { lock (_lock) return _data.StartMinimizedToTray; }
        set { lock (_lock) { _data.StartMinimizedToTray = value; SaveLocked(); } }
    }

    public bool MinimizeToTrayOnClose
    {
        get { lock (_lock) return _data.MinimizeToTrayOnClose; }
        set { lock (_lock) { _data.MinimizeToTrayOnClose = value; SaveLocked(); } }
    }

    public bool IsIpLockedOut(string ip, out TimeSpan remaining)
    {
        lock (_lock)
        {
            remaining = TimeSpan.Zero;
            if (!_data.BlockUnauthorizedAttempts) return false;

            if (_lockouts.TryGetValue(ip, out var lockoutUntil))
            {
                if (DateTime.UtcNow < lockoutUntil)
                {
                    remaining = lockoutUntil - DateTime.UtcNow;
                    return true;
                }
                else
                {
                    _lockouts.Remove(ip);
                    _failedAttempts.Remove(ip);
                }
            }
            return false;
        }
    }

    public void RecordFailedAttempt(string ip)
    {
        lock (_lock)
        {
            if (!_data.BlockUnauthorizedAttempts) return;

            _failedAttempts.TryGetValue(ip, out int count);
            count++;
            _failedAttempts[ip] = count;

            if (count >= _data.MaxFailedAuthAttempts)
            {
                _lockouts[ip] = DateTime.UtcNow.AddMinutes(_data.LockoutDurationMinutes);
            }
        }
    }

    public void ResetFailedAttempts(string ip)
    {
        lock (_lock)
        {
            _failedAttempts.Remove(ip);
            _lockouts.Remove(ip);
        }
    }

    public void ClearAllLockouts()
    {
        lock (_lock)
        {
            _failedAttempts.Clear();
            _lockouts.Clear();
        }
    }

    public int GetActiveLockoutsCount()
    {
        lock (_lock)
        {
            var now = DateTime.UtcNow;
            return _lockouts.Count(kvp => kvp.Value > now);
        }
    }

    public bool TryGetPassword(string? machineName, string? ipAddress, out string password)
    {
        lock (_lock)
        {
            if (!string.IsNullOrWhiteSpace(machineName) && _data.SavedPasswords.TryGetValue(machineName, out var pass1))
            {
                password = pass1;
                return true;
            }

            if (!string.IsNullOrWhiteSpace(ipAddress) && _data.SavedPasswords.TryGetValue(ipAddress, out var pass2))
            {
                password = pass2;
                return true;
            }

            password = string.Empty;
            return false;
        }
    }

    public void SavePassword(string? machineName, string? ipAddress, string password)
    {
        if (string.IsNullOrWhiteSpace(password)) return;

        lock (_lock)
        {
            if (!string.IsNullOrWhiteSpace(machineName))
            {
                _data.SavedPasswords[machineName] = password;
            }

            if (!string.IsNullOrWhiteSpace(ipAddress))
            {
                _data.SavedPasswords[ipAddress] = password;
            }

            SaveLocked();
        }
    }

    public void RemovePassword(string? machineName, string? ipAddress)
    {
        lock (_lock)
        {
            bool changed = false;
            if (!string.IsNullOrWhiteSpace(machineName) && _data.SavedPasswords.Remove(machineName))
            {
                changed = true;
            }
            if (!string.IsNullOrWhiteSpace(ipAddress) && _data.SavedPasswords.Remove(ipAddress))
            {
                changed = true;
            }

            if (changed)
            {
                SaveLocked();
            }
        }
    }

    public bool HasSavedPassword(string? machineName, string? ipAddress)
    {
        return TryGetPassword(machineName, ipAddress, out _);
    }

    private void Load()
    {
        lock (_lock)
        {
            try
            {
                if (File.Exists(_filePath))
                {
                    string json = File.ReadAllText(_filePath);
                    var data = JsonSerializer.Deserialize<SettingsData>(json);
                    if (data != null)
                    {
                        _data = data;
                        if (_data.SavedPasswords == null)
                        {
                            _data.SavedPasswords = new(StringComparer.OrdinalIgnoreCase);
                        }
                    }
                }
            }
            catch
            {
                _data = new SettingsData();
            }
        }
    }

    private void SaveLocked()
    {
        try
        {
            string? dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            string json = JsonSerializer.Serialize(_data, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_filePath, json);
        }
        catch
        {
            // Silently ignore write failures
        }
    }
}
