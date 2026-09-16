using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Media;
using RemoteLAN.Agent.Network;
using RemoteLAN.Protocol.Transport;

namespace RemoteLAN.Agent;

public partial class MainWindow : Window
{
    private readonly AgentServer _server;
    private bool _isServerRunning = true;
    private double _currentFps = 0.0;

    public MainWindow()
    {
        InitializeComponent();

        _server = new AgentServer(ProtocolConstants.DefaultPort);
        _server.StatusChanged += Server_StatusChanged;
        _server.ClientConnected += Server_ClientConnected;
        _server.ClientDisconnected += Server_ClientDisconnected;
        _server.FpsUpdated += Server_FpsUpdated;
        _server.PinManager.PinChanged += PinManager_PinChanged;

        UpdatePinDisplay(_server.PinManager.CurrentPin);
        LoadLocalIpAddresses();

        EngineTextBlock.Text = _server.CaptureEngineName;
        UpdateScreenInfo();

        _server.Start();
    }

    private void UpdateScreenInfo()
    {
        ScreenInfoTextBlock.Text = $"{_server.ScreenWidth}x{_server.ScreenHeight} ({_currentFps:0.0} FPS)";
    }

    private sealed class NetworkAddressItem
    {
        public string IpAddress { get; init; } = string.Empty;
        public string DisplayText { get; init; } = string.Empty;
        public bool IsPrimary { get; init; }

        public override string ToString() => DisplayText;
    }

    private void LoadLocalIpAddresses()
    {
        var items = new List<NetworkAddressItem>();

        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up) continue;
            if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

            var ipProps = ni.GetIPProperties();
            bool hasGateway = ipProps.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork);

            foreach (var unicast in ipProps.UnicastAddresses)
            {
                if (unicast.Address.AddressFamily == AddressFamily.InterNetwork)
                {
                    string ipStr = unicast.Address.ToString();
                    bool isLikelyLan = ipStr.StartsWith("192.168.") || ipStr.StartsWith("10.") ||
                                       (hasGateway && !ipStr.StartsWith("169.254."));

                    items.Add(new NetworkAddressItem
                    {
                        IpAddress = ipStr,
                        DisplayText = $"{ipStr} ({ni.Name})",
                        IsPrimary = isLikelyLan || hasGateway
                    });
                }
            }
        }

        if (items.Count == 0)
        {
            items.Add(new NetworkAddressItem
            {
                IpAddress = "127.0.0.1",
                DisplayText = "127.0.0.1 (Loopback only)",
                IsPrimary = true
            });
        }

        var sorted = items.OrderByDescending(i => i.IsPrimary).ToList();
        LocalIpsComboBox.ItemsSource = sorted;
        LocalIpsComboBox.SelectedIndex = 0;
    }

    private void CopyIp_Click(object sender, RoutedEventArgs e)
    {
        if (LocalIpsComboBox.SelectedItem is NetworkAddressItem item)
        {
            try
            {
                Clipboard.SetText(item.IpAddress);
            }
            catch { }
        }
    }

    private void UpdatePinDisplay(string pin)
    {
        Dispatcher.Invoke(() =>
        {
            PinTextBlock.Text = pin;
        });
    }

    private void PinManager_PinChanged(string pin)
    {
        UpdatePinDisplay(pin);
    }

    private void Server_StatusChanged(string status)
    {
        Dispatcher.Invoke(() =>
        {
            StatusText.Text = status;
        });
    }

    private void Server_ClientConnected(string endpoint)
    {
        Dispatcher.Invoke(() =>
        {
            StatusDot.Background = new SolidColorBrush(Color.FromRgb(59, 130, 246)); // Blue
            ConnectedClientPanel.Visibility = Visibility.Visible;
            ConnectedClientText.Text = endpoint;
        });
    }

    private void Server_ClientDisconnected()
    {
        Dispatcher.Invoke(() =>
        {
            StatusDot.Background = new SolidColorBrush(Color.FromRgb(16, 185, 129)); // Green
            ConnectedClientPanel.Visibility = Visibility.Collapsed;
            _currentFps = 0.0;
            UpdateScreenInfo();
        });
    }

    private void Server_FpsUpdated(double fps)
    {
        Dispatcher.Invoke(() =>
        {
            _currentFps = fps;
            UpdateScreenInfo();
        });
    }

    private void CopyPin_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(_server.PinManager.CurrentPin);
        }
        catch { }
    }

    private void RegeneratePin_Click(object sender, RoutedEventArgs e)
    {
        _server.PinManager.RegeneratePin();
    }

    private void DisconnectClient_Click(object sender, RoutedEventArgs e)
    {
        _server.DisconnectCurrentClient();
    }

    private void ToggleServer_Click(object sender, RoutedEventArgs e)
    {
        if (_isServerRunning)
        {
            _server.Stop();
            _isServerRunning = false;
            ToggleServerBtn.Content = "Start Server";
            StatusDot.Background = new SolidColorBrush(Color.FromRgb(239, 68, 68)); // Red
        }
        else
        {
            _server.Start();
            _isServerRunning = true;
            ToggleServerBtn.Content = "Stop Server";
            StatusDot.Background = new SolidColorBrush(Color.FromRgb(16, 185, 129)); // Green
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _server.Dispose();
    }
}