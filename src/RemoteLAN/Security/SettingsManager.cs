using System.IO;
using System.Text.Json;
using RemoteLAN.Protocol.Discovery;

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
        public Dictionary<string, string> SavedOsPasswords { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public List<DiscoveredDeviceHistoryItem> DeviceHistory { get; set; } = new();
        public bool AutoEnterOsPasswordOnConnect { get; set; } = true;

        // Security / Unauthorized access protection
        public bool BlockUnauthorizedAttempts { get; set; } = true;
        public int MaxFailedAuthAttempts { get; set; } = 5;
        public int LockoutDurationMinutes { get; set; } = 10;
        public int PinRotationIntervalMinutes { get; set; } = 0; // 0 = Never (Default)

        // General application preferences
        public bool StartMinimizedToTray { get; set; } = false;
        public bool MinimizeToTrayOnClose { get; set; } = true;
    }

    public sealed class DiscoveredDeviceHistoryItem
    {
        public string MachineName { get; set; } = string.Empty;
        public string IpAddress { get; set; } = string.Empty;
        public int Port { get; set; } = 9191;
        public string Version { get; set; } = string.Empty;
        public DateTime LastSeenUtc { get; set; } = DateTime.UtcNow;
    }

    private SettingsData _data = new();
    private readonly Dictionary<string, int> _failedAttempts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTime> _lockouts = new(StringComparer.OrdinalIgnoreCase);

    public static string? OverrideFilePath { get; set; }

    public SettingsManager(string? filePath = null)
    {
        _filePath = filePath ?? OverrideFilePath ?? GetDefaultFilePath();
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

    public bool TryGetOsPassword(string? machineName, string? ipAddress, out string osPassword)
    {
        lock (_lock)
        {
            if (!string.IsNullOrWhiteSpace(machineName) && _data.SavedOsPasswords.TryGetValue(machineName, out var pass1))
            {
                osPassword = pass1;
                return true;
            }

            if (!string.IsNullOrWhiteSpace(ipAddress) && _data.SavedOsPasswords.TryGetValue(ipAddress, out var pass2))
            {
                osPassword = pass2;
                return true;
            }

            osPassword = string.Empty;
            return false;
        }
    }

    public void SaveOsPassword(string? machineName, string? ipAddress, string osPassword)
    {
        if (string.IsNullOrWhiteSpace(osPassword)) return;

        lock (_lock)
        {
            if (!string.IsNullOrWhiteSpace(machineName))
            {
                _data.SavedOsPasswords[machineName] = osPassword;
            }

            if (!string.IsNullOrWhiteSpace(ipAddress))
            {
                _data.SavedOsPasswords[ipAddress] = osPassword;
            }

            SaveLocked();
        }
    }

    public void RemoveOsPassword(string? machineName, string? ipAddress)
    {
        lock (_lock)
        {
            bool changed = false;
            if (!string.IsNullOrWhiteSpace(machineName) && _data.SavedOsPasswords.Remove(machineName))
            {
                changed = true;
            }
            if (!string.IsNullOrWhiteSpace(ipAddress) && _data.SavedOsPasswords.Remove(ipAddress))
            {
                changed = true;
            }

            if (changed)
            {
                SaveLocked();
            }
        }
    }

    public bool HasSavedOsPassword(string? machineName, string? ipAddress)
    {
        return TryGetOsPassword(machineName, ipAddress, out _);
    }

    public bool TryGetOsPassword(string target, out string osPassword)
    {
        return TryGetOsPassword(target, target, out osPassword);
    }

    public void SaveOsPassword(string target, string osPassword)
    {
        SaveOsPassword(target, target, osPassword);
    }

    public void RemoveOsPassword(string target)
    {
        RemoveOsPassword(target, target);
    }

    public bool HasSavedOsPassword(string target)
    {
        return HasSavedOsPassword(target, target);
    }

    public List<DiscoveredDeviceHistoryItem> GetDeviceHistory()
    {
        lock (_lock)
        {
            return new List<DiscoveredDeviceHistoryItem>(_data.DeviceHistory);
        }
    }

    public void UpdateDeviceInHistory(string machineName, string ipAddress, int port, string version)
    {
        if (string.IsNullOrWhiteSpace(machineName) || string.IsNullOrWhiteSpace(ipAddress)) return;

        lock (_lock)
        {
            var existing = _data.DeviceHistory.FirstOrDefault(d =>
                string.Equals(d.MachineName, machineName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(d.IpAddress, ipAddress, StringComparison.OrdinalIgnoreCase));

            if (existing != null)
            {
                existing.MachineName = machineName;
                existing.IpAddress = ipAddress;
                existing.Port = port;
                existing.Version = version;
                existing.LastSeenUtc = DateTime.UtcNow;
            }
            else
            {
                _data.DeviceHistory.Add(new DiscoveredDeviceHistoryItem
                {
                    MachineName = machineName,
                    IpAddress = ipAddress,
                    Port = port,
                    Version = version,
                    LastSeenUtc = DateTime.UtcNow
                });
            }

            SaveLocked();
        }
    }

    public void UpdateDeviceInHistory(DiscoveredAgent agent)
    {
        if (agent == null) return;
        UpdateDeviceInHistory(agent.MachineName, agent.IpAddress, agent.Port, agent.Version);
    }

    public void RemoveDeviceFromHistory(string target)
    {
        RemoveDeviceFromHistory(target, target);
    }

    public void RemoveDeviceFromHistory(string? machineName, string? ipAddress)
    {
        RemoveDeviceFromHistory(machineName, ipAddress, 0);
    }

    public void RemoveDeviceFromHistory(string? machineName, string? ipAddress, int port)
    {
        lock (_lock)
        {
            int removed = _data.DeviceHistory.RemoveAll(d =>
                (!string.IsNullOrWhiteSpace(machineName) && string.Equals(d.MachineName, machineName, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrWhiteSpace(ipAddress) && string.Equals(d.IpAddress, ipAddress, StringComparison.OrdinalIgnoreCase) && (port <= 0 || d.Port == port)));

            if (removed > 0)
            {
                SaveLocked();
            }
        }
    }

    public void Save()
    {
        lock (_lock)
        {
            SaveLocked();
        }
    }

    public bool AutoEnterOsPasswordOnConnect
    {
        get { lock (_lock) return _data.AutoEnterOsPasswordOnConnect; }
        set { lock (_lock) { _data.AutoEnterOsPasswordOnConnect = value; SaveLocked(); } }
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
                        if (_data.SavedOsPasswords == null)
                        {
                            _data.SavedOsPasswords = new(StringComparer.OrdinalIgnoreCase);
                        }
                        if (_data.DeviceHistory == null)
                        {
                            _data.DeviceHistory = new();
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
