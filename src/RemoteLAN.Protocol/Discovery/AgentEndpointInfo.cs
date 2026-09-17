using System.ComponentModel;

namespace RemoteLAN.Protocol.Discovery;

public sealed class AgentEndpointInfo : INotifyPropertyChanged
{
    private string _ipAddress = string.Empty;
    private string _interfaceType = "Ethernet";
    private DateTime _lastSeen = DateTime.UtcNow;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

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
            }
        }
    }

    public string InterfaceType
    {
        get => _interfaceType;
        set
        {
            if (_interfaceType != value)
            {
                _interfaceType = value;
                OnPropertyChanged(nameof(InterfaceType));
                OnPropertyChanged(nameof(DisplayText));
                OnPropertyChanged(nameof(IsEthernet));
                OnPropertyChanged(nameof(BadgeBackgroundBrush));
                OnPropertyChanged(nameof(BadgeForegroundBrush));
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
            }
        }
    }

    public bool IsEthernet => string.Equals(InterfaceType, "Ethernet", StringComparison.OrdinalIgnoreCase);

    public string BadgeBackgroundBrush => IsEthernet ? "#EFF6FF" : "#F0FDF4"; // Light blue for Ethernet, light green for WiFi
    public string BadgeForegroundBrush => IsEthernet ? "#2563EB" : "#16A34A";

    public string DisplayText => $"{IpAddress} ({InterfaceType})";

    public override string ToString() => DisplayText;
}
