using System.ComponentModel;

namespace RemoteLAN.Protocol.Discovery;

public sealed class DiscoveredAgent : INotifyPropertyChanged
{
    private string _machineName = string.Empty;
    private string _ipAddress = string.Empty;
    private int _port;
    private string _version = string.Empty;
    private bool _isOnline = true;
    private DateTime _lastSeen = DateTime.UtcNow;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    public string MachineName
    {
        get => _machineName;
        set
        {
            if (_machineName != value)
            {
                _machineName = value;
                OnPropertyChanged(nameof(MachineName));
                OnPropertyChanged(nameof(DisplayText));
                OnPropertyChanged(nameof(HeaderBackgroundBrush));
            }
        }
    }

    public string IpAddress
    {
        get => _ipAddress;
        set
        {
            if (_ipAddress != value)
            {
                _ipAddress = value;
                OnPropertyChanged(nameof(IpAddress));
                OnPropertyChanged(nameof(DisplayText));
                OnPropertyChanged(nameof(HeaderBackgroundBrush));
            }
        }
    }

    public int Port
    {
        get => _port;
        set
        {
            if (_port != value)
            {
                _port = value;
                OnPropertyChanged(nameof(Port));
                OnPropertyChanged(nameof(DisplayText));
            }
        }
    }

    public string Version
    {
        get => _version;
        set
        {
            if (_version != value)
            {
                _version = value;
                OnPropertyChanged(nameof(Version));
            }
        }
    }

    public bool IsOnline
    {
        get => _isOnline;
        set
        {
            if (_isOnline != value)
            {
                _isOnline = value;
                OnPropertyChanged(nameof(IsOnline));
                OnPropertyChanged(nameof(StatusDotBrush));
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(CardOpacity));
            }
        }
    }

    public DateTime LastSeen
    {
        get => _lastSeen;
        set
        {
            if (_lastSeen != value)
            {
                _lastSeen = value;
                OnPropertyChanged(nameof(LastSeen));
                OnPropertyChanged(nameof(StatusText));
            }
        }
    }

    public string DisplayText => $"{MachineName} ({IpAddress}:{Port})";

    public string HeaderBackgroundBrush
    {
        get
        {
            // Deterministic pastel palette matching AnyDesk cards.png
            string[] palette = new[]
            {
                "#8497A0", // Slate teal (from cards.png left card)
                "#958CDD", // Soft periwinkle (from cards.png right card)
                "#7E97A6", // Steel blue
                "#8A7EBE", // Soft indigo
                "#6E929E", // Muted ocean
                "#8FA08B", // Sage green
                "#A0897B", // Warm clay
                "#9C7E92"  // Dusty mauve
            };
            int hash = Math.Abs((MachineName + IpAddress).GetHashCode());
            return palette[hash % palette.Length];
        }
    }

    public string StatusDotBrush => IsOnline ? "#22C55E" : "#94A3B8"; // Green when online, Slate Gray when offline
    public string StatusText => IsOnline ? "Online" : $"Offline • Last seen {LastSeen:g}";
    public double CardOpacity => IsOnline ? 1.0 : 0.65;

    public override string ToString() => DisplayText;

    public static bool TryParse(string rawMessage, string senderIp, out DiscoveredAgent? agent)
    {
        agent = null;
        if (string.IsNullOrWhiteSpace(rawMessage) || !rawMessage.StartsWith(DiscoveryConstants.DiscoveryResponsePrefix, StringComparison.Ordinal))
        {
            return false;
        }

        // Expected format: REMOTELAN_AGENT_V1|MachineName|TcpPort|Version
        string payload = rawMessage.Substring(DiscoveryConstants.DiscoveryResponsePrefix.Length);
        string[] parts = payload.Split('|');

        if (parts.Length < 2)
        {
            return false;
        }

        string machineName = parts[0].Trim();
        if (!int.TryParse(parts[1].Trim(), out int port) || port <= 0 || port > 65535)
        {
            return false;
        }

        string version = parts.Length >= 3 ? parts[2].Trim() : "unknown";

        agent = new DiscoveredAgent
        {
            MachineName = machineName,
            IpAddress = senderIp,
            Port = port,
            Version = version
        };

        return true;
    }
}
