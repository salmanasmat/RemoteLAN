using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RemoteLAN.Discovery;
using RemoteLAN.Network;
using RemoteLAN.Protocol.Discovery;
using RemoteLAN.Protocol.Transport;
using RemoteLAN.Views;

namespace RemoteLAN;

public partial class MainWindow : Window
{
    private readonly AgentServer _server;
    private readonly LanDiscoveryClient _discoveryClient = new();

    private sealed class NetworkAddressItem
    {
        public string IpAddress { get; init; } = string.Empty;
        public string DisplayText { get; init; } = string.Empty;
        public bool IsPrimary { get; init; }

        public override string ToString() => DisplayText;
    }

    public MainWindow()
    {
        InitializeComponent();

        HostDeviceNameText.Text = Environment.MachineName;

        // Initialize and start background host server (always ready for incoming connections)
        _server = new AgentServer(ProtocolConstants.DefaultPort);
        _server.StatusChanged += Server_StatusChanged;
        _server.ClientConnected += Server_ClientConnected;
        _server.ClientDisconnected += Server_ClientDisconnected;
        _server.PinManager.PinChanged += PinManager_PinChanged;

        HostEngineText.Text = _server.CaptureEngineName;
        UpdatePinDisplay(_server.PinManager.CurrentPin);
        LoadLocalIpAddresses();

        _server.Start();

        // Perform initial background scan for other PCs on LAN
        _ = PerformDiscoveryScanAsync();
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
            HostStatusText.Text = status;
        });
    }

    private void Server_ClientConnected(string endpoint)
    {
        Dispatcher.Invoke(() =>
        {
            HostStatusDot.Background = new SolidColorBrush(Color.FromRgb(59, 130, 246)); // Blue
            ActiveClientCard.Visibility = Visibility.Visible;
            ActiveClientEndpointText.Text = endpoint;
        });
    }

    private void Server_ClientDisconnected()
    {
        Dispatcher.Invoke(() =>
        {
            HostStatusDot.Background = new SolidColorBrush(Color.FromRgb(16, 185, 129)); // Green
            ActiveClientCard.Visibility = Visibility.Collapsed;
        });
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

    private void DisconnectIncomingClient_Click(object sender, RoutedEventArgs e)
    {
        _server.DisconnectCurrentClient();
    }

    private async void ScanLanBtn_Click(object sender, RoutedEventArgs e)
    {
        await PerformDiscoveryScanAsync();
    }

    private async Task PerformDiscoveryScanAsync()
    {
        ScanLanBtn.IsEnabled = false;
        ConnectionStatusText.Text = "Scanning LAN for running PCs...";

        try
        {
            var agents = await _discoveryClient.DiscoverAgentsAsync();
            DiscoveredPcsComboBox.ItemsSource = agents;

            if (agents.Count > 0)
            {
                DiscoveredPcsComboBox.SelectedIndex = 0;
                ConnectionStatusText.Text = $"Discovered {agents.Count} PC(s) on LAN";
            }
            else
            {
                ConnectionStatusText.Text = "No other RemoteLAN PCs found on LAN";
            }
        }
        catch
        {
            ConnectionStatusText.Text = "LAN scan complete";
        }
        finally
        {
            ScanLanBtn.IsEnabled = true;
        }
    }

    private void DiscoveredPcsComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DiscoveredPcsComboBox.SelectedItem is DiscoveredAgent agent)
        {
            TargetIpTextBox.Text = agent.IpAddress;
            TargetPortTextBox.Text = agent.Port.ToString();
        }
    }

    private async void ConnectRemoteBtn_Click(object sender, RoutedEventArgs e)
    {
        string ip = TargetIpTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(ip))
        {
            MessageBox.Show("Please enter the remote PC's IP address.", "Connection Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!int.TryParse(TargetPortTextBox.Text.Trim(), out int port) || port <= 0 || port > 65535)
        {
            port = ProtocolConstants.DefaultPort;
            TargetPortTextBox.Text = port.ToString();
        }

        string pin = RemotePinTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(pin))
        {
            MessageBox.Show("Please enter the remote PC's Security PIN.", "Connection Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ConnectRemoteBtn.IsEnabled = false;
        ConnectionStatusText.Text = $"Connecting to {ip}:{port}...";

        var client = new ControllerClient();
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        void StateHandler(ControllerState state, string message)
        {
            if (state == ControllerState.Connected)
            {
                tcs.TrySetResult(true);
            }
            else if (state == ControllerState.Error || state == ControllerState.Disconnected)
            {
                tcs.TrySetResult(false);
            }
        }

        client.StateChanged += StateHandler;

        try
        {
            await client.ConnectAsync(ip, port, pin);
            bool connected = await tcs.Task;

            client.StateChanged -= StateHandler;

            if (connected)
            {
                ConnectionStatusText.Text = "Connected!";

                string displayName = (DiscoveredPcsComboBox.SelectedItem is DiscoveredAgent agent && agent.IpAddress == ip)
                    ? agent.MachineName
                    : ip;

                var sessionWin = new SessionWindow(client, displayName, $"{ip}:{port}");
                sessionWin.Show();
                ConnectionStatusText.Text = "Ready to connect";
            }
            else
            {
                ConnectionStatusText.Text = "Connection failed";
                MessageBox.Show($"Could not connect to {ip}:{port}. Check that the remote PC has RemoteLAN running and the PIN is correct.", "Connection Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                client.Dispose();
            }
        }
        catch (Exception ex)
        {
            client.StateChanged -= StateHandler;
            ConnectionStatusText.Text = $"Error: {ex.Message}";
            MessageBox.Show($"Connection error: {ex.Message}", "Connection Error", MessageBoxButton.OK, MessageBoxImage.Error);
            client.Dispose();
        }
        finally
        {
            ConnectRemoteBtn.IsEnabled = true;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _server.Dispose();
    }
}