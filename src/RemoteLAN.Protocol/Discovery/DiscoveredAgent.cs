using System.Collections.ObjectModel;
using System.ComponentModel;

namespace RemoteLAN.Protocol.Discovery;

public sealed class DiscoveredAgent : INotifyPropertyChanged
{
    private string _machineId = string.Empty;
    private string _machineName = string.Empty;
    private string _ipAddress = string.Empty;
    private int _port;
    private string _version = string.Empty;
    private bool _isOnline = true;
    private DateTime _lastSeen = DateTime.UtcNow;
    private AgentEndpointInfo? _selectedEndpoint;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    public ObservableCollection<AgentEndpointInfo> Endpoints { get; } = new();

    public string MachineId
    {
        get => string.IsNullOrEmpty(_machineId) ? _machineName : _machineId;
        set
        {
            if (_machineId != value)
            {
                _machineId = value;
                OnPropertyChanged(nameof(MachineId));
                OnPropertyChanged(nameof(HeaderBackgroundBrush));
            }
        }
    }

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
        get => _selectedEndpoint?.IpAddress ?? _ipAddress;
        set
        {
            if (_ipAddress != value)
            {
                _ipAddress = value;
                if (!string.IsNullOrEmpty(value) && !Endpoints.Any(e => e.IpAddress.Equals(value, StringComparison.OrdinalIgnoreCase)))
                {
                    AddOrUpdateEndpoint(value, "Ethernet");
                }
                else
                {
                    var existing = Endpoints.FirstOrDefault(e => e.IpAddress.Equals(value, StringComparison.OrdinalIgnoreCase));
                    if (existing != null && _selectedEndpoint != existing)
                    {
                        _selectedEndpoint = existing;
                        OnPropertyChanged(nameof(SelectedEndpoint));
                        OnPropertyChanged(nameof(InterfaceType));
                        OnPropertyChanged(nameof(IsEthernet));
                    }
                }
                OnPropertyChanged(nameof(IpAddress));
                OnPropertyChanged(nameof(DisplayText));
            }
        }
    }

    public AgentEndpointInfo? SelectedEndpoint
    {
        get => _selectedEndpoint;
        set
        {
            if (_selectedEndpoint != value)
            {
                _selectedEndpoint = value;
                if (value != null)
                {
                    _ipAddress = value.IpAddress;
                }
                OnPropertyChanged(nameof(SelectedEndpoint));
                OnPropertyChanged(nameof(IpAddress));
                OnPropertyChanged(nameof(InterfaceType));
                OnPropertyChanged(nameof(IsEthernet));
                OnPropertyChanged(nameof(DisplayText));
            }
        }
    }

    public string InterfaceType => _selectedEndpoint?.InterfaceType ?? "Ethernet";

    public bool IsEthernet => string.Equals(InterfaceType, "Ethernet", StringComparison.OrdinalIgnoreCase);

    public bool HasMultipleEndpoints => Endpoints.Count > 1;

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
            string key = string.IsNullOrEmpty(MachineId) ? (MachineName + IpAddress) : MachineId;
            int hash = Math.Abs(key.GetHashCode());
            return palette[hash % palette.Length];
        }
    }

    public string StatusDotBrush => IsOnline ? "#22C55E" : "#94A3B8"; // Green when online, Slate Gray when offline
    public string StatusText => IsOnline ? "Online" : $"Offline • Last seen {LastSeen:g}";
    public double CardOpacity => IsOnline ? 1.0 : 0.65;

    public void AddOrUpdateEndpoint(string ip, string interfaceType, DateTime? lastSeen = null)
    {
        if (string.IsNullOrWhiteSpace(ip)) return;

        DateTime seenTime = lastSeen ?? DateTime.UtcNow;
        var existing = Endpoints.FirstOrDefault(e => e.IpAddress.Equals(ip, StringComparison.OrdinalIgnoreCase));

        if (existing != null)
        {
            existing.InterfaceType = interfaceType;
            existing.LastSeen = seenTime;
        }
        else
        {
            var newEndpoint = new AgentEndpointInfo
            {
                IpAddress = ip,
                InterfaceType = interfaceType,
                LastSeen = seenTime
            };
            Endpoints.Add(newEndpoint);
            OnPropertyChanged(nameof(HasMultipleEndpoints));
        }

        LastSeen = seenTime;

        // Auto-select preferred endpoint:
        // 1. If nothing selected yet or selected endpoint is no longer in Endpoints
        // 2. Or if current selection is not Ethernet but an Ethernet endpoint is now available
        if (SelectedEndpoint == null || !Endpoints.Contains(SelectedEndpoint) ||
            (!SelectedEndpoint.IsEthernet && Endpoints.Any(e => e.IsEthernet)))
        {
            // Prefer Ethernet over WiFi, then most recently seen
            var preferred = Endpoints
                .OrderByDescending(e => e.IsEthernet)
                .ThenByDescending(e => e.LastSeen)
                .FirstOrDefault();

            if (preferred != null)
            {
                SelectedEndpoint = preferred;
            }
        }
    }

    public override string ToString() => DisplayText;

    public static bool TryParse(string rawMessage, string senderIp, out DiscoveredAgent? agent)
    {
        agent = null;
        if (string.IsNullOrWhiteSpace(rawMessage) || !rawMessage.StartsWith(DiscoveryConstants.DiscoveryResponsePrefix, StringComparison.Ordinal))
        {
            return false;
        }

        // Expected format: REMOTELAN_AGENT_V1|MachineName|TcpPort|Version[|MachineId[|InterfaceType[|IpAddress]]]
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
        string machineId = parts.Length >= 4 && !string.IsNullOrWhiteSpace(parts[3]) ? parts[3].Trim() : machineName;
        string interfaceType = parts.Length >= 5 && !string.IsNullOrWhiteSpace(parts[4]) ? parts[4].Trim() : "Ethernet";
        string endpointIp = parts.Length >= 6 && !string.IsNullOrWhiteSpace(parts[5]) ? parts[5].Trim() : senderIp;

        agent = new DiscoveredAgent
        {
            MachineName = machineName,
            MachineId = machineId,
            Port = port,
            Version = version
        };

        agent.AddOrUpdateEndpoint(endpointIp, interfaceType);
        return true;
    }
}
