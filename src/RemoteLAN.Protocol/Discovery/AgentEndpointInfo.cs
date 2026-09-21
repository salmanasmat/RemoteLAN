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
                OnPropertyChanged(nameof(IconData));
                OnPropertyChanged(nameof(IconBrush));
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

    public string IconData => IsEthernet
        ? "M 3,3 L 17,3 C 18.1,3 19,3.9 19,5 L 19,13 C 19,14.1 18.1,15 17,15 L 13,15 L 13,18 L 15,18 L 15,20 L 5,20 L 5,18 L 7,18 L 7,15 L 3,15 C 1.9,15 1,14.1 1,13 L 1,5 C 1,3.9 1.9,3 3,3 Z M 4,6 L 4,11 L 16,11 L 16,6 Z M 6,8 L 7.5,8 L 7.5,10 L 6,10 Z M 9.25,8 L 10.75,8 L 10.75,10 L 9.25,10 Z M 12.5,8 L 14,8 L 14,10 L 12.5,10 Z"
        : "M 12,18 A 1.5,1.5 0 1,1 12,21 A 1.5,1.5 0 1,1 12,18 Z M 7.05,14.05 C 9.78,11.32 14.22,11.32 16.95,14.05 L 18.36,12.64 C 14.85,9.13 9.15,9.13 5.64,12.64 Z M 3.51,10.51 C 8.2,5.82 15.8,5.82 20.49,10.51 L 21.9,9.1 C 16.43,3.63 7.57,3.63 2.1,9.1 Z";

    public string IconBrush => IsEthernet ? "#2563EB" : "#16A34A";

    public string DisplayText => $"{IpAddress} ({InterfaceType})";

    public override string ToString() => DisplayText;
}
