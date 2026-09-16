using System.Collections.ObjectModel;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
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
    private readonly ObservableCollection<DiscoveredAgent> _discoveredAgents = new();
    private readonly DispatcherTimer _discoveryTimer = new();
    private readonly HashSet<string> _localIpAddresses = new(StringComparer.OrdinalIgnoreCase) { "127.0.0.1", "localhost", "::1" };
    private DiscoveredAgent? _modalTargetAgent;
    private bool _isScanning;

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

        UpdatePinDisplay(_server.PinManager.CurrentPin);
        LoadLocalIpAddresses();

        _server.Start();

        // Bind discovered PCs collection to the AnyDesk-style grid
        DiscoveredPcsListBox.ItemsSource = _discoveredAgents;
        UpdateEmptyState();

        // Setup continuous automatic LAN discovery (runs every 5 seconds)
        _discoveryTimer.Interval = TimeSpan.FromSeconds(5);
        _discoveryTimer.Tick += async (s, e) => await PerformDiscoveryScanAsync();
        _discoveryTimer.Start();

        // Trigger immediate scan upon application launch
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
                    _localIpAddresses.Add(ipStr);

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
                DisplayText = "127.0.0.1 (Loopback)",
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
            // Always display clean, consumer-friendly status without ports or technical engines
            if (status.Contains("Stopped", StringComparison.OrdinalIgnoreCase))
            {
                HostStatusText.Text = "Host Offline";
                HostStatusDot.Background = new SolidColorBrush(Color.FromRgb(239, 68, 68)); // Red
            }
            else if (status.Contains("Connecting", StringComparison.OrdinalIgnoreCase) ||
                     status.Contains("Connected", StringComparison.OrdinalIgnoreCase))
            {
                HostStatusText.Text = "Session Active";
                HostStatusDot.Background = new SolidColorBrush(Color.FromRgb(59, 130, 246)); // Blue
            }
            else
            {
                HostStatusText.Text = "Ready for connections";
                HostStatusDot.Background = new SolidColorBrush(Color.FromRgb(16, 185, 129)); // Emerald Green
            }
        });
    }

    private void Server_ClientConnected(string endpoint)
    {
        Dispatcher.Invoke(() =>
        {
            HostStatusDot.Background = new SolidColorBrush(Color.FromRgb(59, 130, 246)); // Blue
            HostStatusText.Text = "Session Active";
            ActiveClientCard.Visibility = Visibility.Visible;
            ActiveClientEndpointText.Text = endpoint;
        });
    }

    private void Server_ClientDisconnected()
    {
        Dispatcher.Invoke(() =>
        {
            HostStatusDot.Background = new SolidColorBrush(Color.FromRgb(16, 185, 129)); // Green
            HostStatusText.Text = "Ready for connections";
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

    private async Task PerformDiscoveryScanAsync()
    {
        if (_isScanning) return;
        _isScanning = true;

        try
        {
            // Discover remote agents, explicitly filtering out this machine's own network interfaces
            var rawAgents = await _discoveryClient.DiscoverAgentsAsync(filterSelf: true);

            // Double filter against local machine name, loopback, and local IPs
            var filteredAgents = rawAgents.Where(a =>
                !string.Equals(a.MachineName, Environment.MachineName, StringComparison.OrdinalIgnoreCase) &&
                !_localIpAddresses.Contains(a.IpAddress) &&
                a.IpAddress != "127.0.0.1"
            ).ToList();

            Dispatcher.Invoke(() =>
            {
                // Remove stale devices
                for (int i = _discoveredAgents.Count - 1; i >= 0; i--)
                {
                    var existing = _discoveredAgents[i];
                    if (!filteredAgents.Any(a => a.IpAddress.Equals(existing.IpAddress, StringComparison.OrdinalIgnoreCase) && a.Port == existing.Port))
                    {
                        _discoveredAgents.RemoveAt(i);
                    }
                }

                // Add newly discovered devices or update existing
                foreach (var agent in filteredAgents)
                {
                    var existing = _discoveredAgents.FirstOrDefault(a => a.IpAddress.Equals(agent.IpAddress, StringComparison.OrdinalIgnoreCase) && a.Port == agent.Port);
                    if (existing == null)
                    {
                        _discoveredAgents.Add(agent);
                    }
                    else if (existing.MachineName != agent.MachineName || existing.Version != agent.Version)
                    {
                        int index = _discoveredAgents.IndexOf(existing);
                        _discoveredAgents[index] = agent;
                    }
                }

                UpdateEmptyState();
                UpdateDiscoveredCount();
            });
        }
        catch
        {
            // Transient discovery scan errors ignored gracefully
        }
        finally
        {
            _isScanning = false;
        }
    }

    private void UpdateEmptyState()
    {
        if (_discoveredAgents.Count == 0)
        {
            DiscoveredEmptyStateBorder.Visibility = Visibility.Visible;
            DiscoveredPcsListBox.Visibility = Visibility.Collapsed;
        }
        else
        {
            DiscoveredEmptyStateBorder.Visibility = Visibility.Collapsed;
            DiscoveredPcsListBox.Visibility = Visibility.Visible;
        }
    }

    private void UpdateDiscoveredCount()
    {
        if (_discoveredAgents.Count == 0)
        {
            DiscoveredDevicesCountText.Text = "Scanning local network...";
        }
        else if (_discoveredAgents.Count == 1)
        {
            DiscoveredDevicesCountText.Text = "1 remote device found on LAN";
        }
        else
        {
            DiscoveredDevicesCountText.Text = $"{_discoveredAgents.Count} remote devices found on LAN";
        }
    }

    // =========================================================================
    // DISCOVERED CARD SELECTION / CONNECTION LOGIC
    // Top inputs are NOT modified when connecting via discovered cards.
    // Instead, a dedicated PIN popup is displayed.
    // =========================================================================

    private void DiscoveredPcsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DiscoveredPcsListBox.SelectedItem is DiscoveredAgent agent)
        {
            DiscoveredPcsListBox.SelectedItem = null;
            OpenPinModalForAgent(agent);
        }
    }

    private void DiscoveredPcsListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // Double-click is handled by SelectionChanged or CardConnectBtn_Click
    }

    private void CardConnectBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement elem && elem.DataContext is DiscoveredAgent agent)
        {
            OpenPinModalForAgent(agent);
        }
    }

    private void OpenPinModalForAgent(DiscoveredAgent agent)
    {
        _modalTargetAgent = agent;
        ModalDeviceNameText.Text = agent.MachineName;
        ModalDeviceIpText.Text = agent.IpAddress;
        ModalPinTextBox.Text = string.Empty;
        ModalStatusText.Visibility = Visibility.Collapsed;
        ModalStatusText.Text = string.Empty;
        ModalConnectBtn.IsEnabled = true;

        PinModalOverlay.Visibility = Visibility.Visible;
        ModalPinTextBox.Focus();
    }

    private void ClosePinModal_Click(object sender, RoutedEventArgs e)
    {
        PinModalOverlay.Visibility = Visibility.Collapsed;
        _modalTargetAgent = null;
    }

    private void PinModalOverlay_MouseDown(object sender, MouseButtonEventArgs e)
    {
        // Close modal when user clicks outside the modal card (on the backdrop)
        if (e.OriginalSource == PinModalOverlay)
        {
            ClosePinModal_Click(this, new RoutedEventArgs());
        }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && PinModalOverlay.Visibility == Visibility.Visible)
        {
            ClosePinModal_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
    }

    private void ModalPinTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ModalConnectBtn_Click(this, new RoutedEventArgs());
        }
        else if (e.Key == Key.Escape)
        {
            ClosePinModal_Click(this, new RoutedEventArgs());
        }
    }

    private async void ModalConnectBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_modalTargetAgent == null) return;

        string pin = ModalPinTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(pin))
        {
            ModalStatusText.Text = "Please enter the 6-digit security PIN.";
            ModalStatusText.Visibility = Visibility.Visible;
            ModalPinTextBox.Focus();
            return;
        }

        ModalConnectBtn.IsEnabled = false;
        ModalStatusText.Visibility = Visibility.Collapsed;
        SetStatus($"Connecting to {_modalTargetAgent.MachineName}...", Color.FromRgb(59, 130, 246));

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

        string ip = _modalTargetAgent.IpAddress;
        int port = _modalTargetAgent.Port;
        string machineName = _modalTargetAgent.MachineName;

        try
        {
            await client.ConnectAsync(ip, port, pin);
            bool connected = await tcs.Task;

            client.StateChanged -= StateHandler;

            if (connected)
            {
                SetStatus($"Connected to {machineName}", Color.FromRgb(16, 185, 129));
                PinModalOverlay.Visibility = Visibility.Collapsed;

                var sessionWin = new SessionWindow(client, machineName, $"{ip}:{port}");
                sessionWin.Show();
                SetStatus("Ready for connections", Color.FromRgb(16, 185, 129));
            }
            else
            {
                SetStatus("Connection failed", Color.FromRgb(239, 68, 68));
                ModalStatusText.Text = "Could not connect. Check PIN or ensure RemoteLAN is running.";
                ModalStatusText.Visibility = Visibility.Visible;
                client.Dispose();
                ModalConnectBtn.IsEnabled = true;
            }
        }
        catch (Exception ex)
        {
            client.StateChanged -= StateHandler;
            SetStatus($"Error: {ex.Message}", Color.FromRgb(239, 68, 68));
            ModalStatusText.Text = $"Connection error: {ex.Message}";
            ModalStatusText.Visibility = Visibility.Visible;
            client.Dispose();
            ModalConnectBtn.IsEnabled = true;
        }
    }

    // =========================================================================
    // MANUAL TOP HEADER CONNECTION LOGIC
    // =========================================================================

    private void ConnectionInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ConnectRemoteBtn_Click(this, new RoutedEventArgs());
        }
    }

    private void SetStatus(string message, Color dotColor)
    {
        ConnectionStatusText.Text = message;
        ConnectionStatusDot.Background = new SolidColorBrush(dotColor);
    }

    private async void ConnectRemoteBtn_Click(object sender, RoutedEventArgs e)
    {
        string rawAddress = TargetIpTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(rawAddress))
        {
            MessageBox.Show("Please enter the remote PC's IP address or select a discovered device.", "Connection Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string ip = rawAddress;
        int port = ProtocolConstants.DefaultPort;

        // Support optional IP:Port format without exposing port boxes in standard UI
        if (rawAddress.Contains(':'))
        {
            var parts = rawAddress.Split(':');
            ip = parts[0].Trim();
            if (parts.Length > 1 && int.TryParse(parts[1].Trim(), out int customPort) && customPort > 0 && customPort <= 65535)
            {
                port = customPort;
            }
        }

        string pin = RemotePinTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(pin))
        {
            MessageBox.Show("Please enter the remote PC's Security PIN.", "Connection Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            RemotePinTextBox.Focus();
            return;
        }

        ConnectRemoteBtn.IsEnabled = false;
        SetStatus($"Connecting to {ip}...", Color.FromRgb(59, 130, 246)); // Blue

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
                string displayName = _discoveredAgents.FirstOrDefault(a => a.IpAddress.Equals(ip, StringComparison.OrdinalIgnoreCase))?.MachineName ?? ip;
                SetStatus($"Connected to {displayName}", Color.FromRgb(16, 185, 129)); // Green

                var sessionWin = new SessionWindow(client, displayName, $"{ip}:{port}");
                sessionWin.Show();
                SetStatus("Ready for connections", Color.FromRgb(16, 185, 129));
            }
            else
            {
                SetStatus("Connection failed", Color.FromRgb(239, 68, 68)); // Red
                MessageBox.Show($"Could not connect to {ip}. Please check that RemoteLAN is active on that machine and the Security PIN is correct.", "Connection Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                client.Dispose();
                SetStatus("Ready for connections", Color.FromRgb(16, 185, 129));
            }
        }
        catch (Exception ex)
        {
            client.StateChanged -= StateHandler;
            SetStatus($"Error: {ex.Message}", Color.FromRgb(239, 68, 68)); // Red
            MessageBox.Show($"Connection error: {ex.Message}", "Connection Error", MessageBoxButton.OK, MessageBoxImage.Error);
            client.Dispose();
            SetStatus("Ready for connections", Color.FromRgb(16, 185, 129));
        }
        finally
        {
            ConnectRemoteBtn.IsEnabled = true;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _discoveryTimer.Stop();
        _server.Dispose();
        base.OnClosed(e);
    }
}