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
using RemoteLAN.Security;
using RemoteLAN.Views;

namespace RemoteLAN;

public partial class MainWindow : Window
{
    private readonly SettingsManager _settingsManager = new();
    private readonly AgentServer _server;
    private readonly LanDiscoveryClient _discoveryClient = new();
    private readonly ObservableCollection<DiscoveredAgent> _discoveredAgents = new();
    private readonly DispatcherTimer _discoveryTimer = new();
    private readonly HashSet<string> _localIpAddresses = new(StringComparer.OrdinalIgnoreCase) { "127.0.0.1", "localhost", "::1" };
    private string? _modalTargetIp;
    private int _modalTargetPort;
    private string? _modalTargetDisplayName;
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

        // Load persistent host PIN if previously saved, otherwise AgentServer generates one and saves it
        string? savedHostPin = _settingsManager.GetHostPin();
        _server = new AgentServer(ProtocolConstants.DefaultPort, initialPin: savedHostPin);

        // If this is the first run and a PIN was newly generated, persist it
        if (string.IsNullOrWhiteSpace(savedHostPin))
        {
            _settingsManager.SaveHostPin(_server.PinManager.CurrentPin);
        }

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
        _settingsManager.SaveHostPin(pin);
        UpdatePinDisplay(pin);
    }

    private void Server_StatusChanged(string status)
    {
    }

    private void Server_ClientConnected(string endpoint)
    {
        Dispatcher.Invoke(() =>
        {
            ActiveClientCard.Visibility = Visibility.Visible;
            ActiveClientEndpointText.Text = endpoint;
            SetStatus($"Connected: viewer from {endpoint}", Color.FromRgb(59, 130, 246)); // Blue
        });
    }

    private void Server_ClientDisconnected()
    {
        Dispatcher.Invoke(() =>
        {
            ActiveClientCard.Visibility = Visibility.Collapsed;
            SetStatus("Ready to connect", Color.FromRgb(16, 185, 129)); // Green
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
    // If a saved password exists, connect immediately without entering PIN!
    // Otherwise, open the PIN popup modal.
    // =========================================================================

    private async void DiscoveredPcsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DiscoveredPcsListBox.SelectedItem is DiscoveredAgent agent)
        {
            DiscoveredPcsListBox.SelectedItem = null;
            await HandleAgentCardClickAsync(agent);
        }
    }

    private void DiscoveredPcsListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // Handled via selection or card click
    }

    private async void CardConnectBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement elem && elem.DataContext is DiscoveredAgent agent)
        {
            await HandleAgentCardClickAsync(agent);
        }
    }

    private async Task HandleAgentCardClickAsync(DiscoveredAgent agent)
    {
        // Check if we already have a saved password for this device
        if (_settingsManager.TryGetPassword(agent.MachineName, agent.IpAddress, out string savedPassword) && !string.IsNullOrWhiteSpace(savedPassword))
        {
            SetStatus($"Connecting to {agent.MachineName} (using saved password)...", Color.FromRgb(59, 130, 246));
            var (success, _) = await ConnectWithCredentialsAsync(agent.IpAddress, agent.Port, savedPassword, agent.MachineName);

            if (!success)
            {
                // Saved password failed (e.g. host changed its PIN) -> open PIN modal prompting for new PIN
                OpenPinModal(agent.IpAddress, agent.Port, agent.MachineName, fallbackFromFailedSavedPassword: true);
            }
        }
        else
        {
            OpenPinModal(agent.IpAddress, agent.Port, agent.MachineName);
        }
    }

    private void OpenPinModal(string ip, int port, string displayName, bool fallbackFromFailedSavedPassword = false)
    {
        _modalTargetIp = ip;
        _modalTargetPort = port;
        _modalTargetDisplayName = displayName;

        ModalDeviceNameText.Text = displayName;
        ModalDeviceIpText.Text = port == ProtocolConstants.DefaultPort ? ip : $"{ip}:{port}";
        ModalPinTextBox.Text = string.Empty;
        ModalConnectBtn.IsEnabled = true;

        bool hasSaved = _settingsManager.HasSavedPassword(displayName, ip);
        ModalForgetPasswordBtn.Visibility = hasSaved ? Visibility.Visible : Visibility.Collapsed;

        if (fallbackFromFailedSavedPassword)
        {
            ModalStatusText.Text = "Saved PIN was rejected. Please enter the current PIN:";
            ModalStatusText.Visibility = Visibility.Visible;
        }
        else
        {
            ModalStatusText.Visibility = Visibility.Collapsed;
            ModalStatusText.Text = string.Empty;
        }

        SaveModalPasswordCheckBox.IsChecked = true;
        PinModalOverlay.Visibility = Visibility.Visible;
        ModalPinTextBox.Focus();
    }

    private void ClosePinModal_Click(object sender, RoutedEventArgs e)
    {
        PinModalOverlay.Visibility = Visibility.Collapsed;
        _modalTargetIp = null;
        _modalTargetDisplayName = null;
    }

    private void ModalForgetPasswordBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_modalTargetIp))
        {
            _settingsManager.RemovePassword(_modalTargetDisplayName, _modalTargetIp);
            ModalForgetPasswordBtn.Visibility = Visibility.Collapsed;
            ModalStatusText.Text = "Saved password removed.";
            ModalStatusText.Visibility = Visibility.Visible;
        }
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
        if (string.IsNullOrWhiteSpace(_modalTargetIp)) return;

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

        string ip = _modalTargetIp;
        int port = _modalTargetPort;
        string displayName = _modalTargetDisplayName ?? ip;

        bool remember = SaveModalPasswordCheckBox.IsChecked == true;
        var (connected, errorMsg) = await ConnectWithCredentialsAsync(ip, port, pin, displayName);

        if (connected)
        {
            if (remember)
            {
                _settingsManager.SavePassword(displayName, ip, pin);
            }
            PinModalOverlay.Visibility = Visibility.Collapsed;
            _modalTargetIp = null;
            _modalTargetDisplayName = null;
        }
        else
        {
            if (errorMsg != null && errorMsg.Contains("Authentication", StringComparison.OrdinalIgnoreCase))
            {
                ModalStatusText.Text = "Incorrect security PIN. Please check the PIN on the remote PC.";
            }
            else
            {
                ModalStatusText.Text = "Could not connect. Check PIN or ensure RemoteLAN is running.";
            }
            ModalStatusText.Visibility = Visibility.Visible;
            ModalConnectBtn.IsEnabled = true;
            ModalPinTextBox.Focus();
            ModalPinTextBox.SelectAll();
        }
    }

    private async Task<(bool Success, string? ErrorMessage)> ConnectWithCredentialsAsync(string ip, int port, string pin, string displayName)
    {
        SetStatus($"Connecting to {displayName}...", Color.FromRgb(59, 130, 246));

        var client = new ControllerClient();
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        string? failureReason = null;

        void StateHandler(ControllerState state, string message)
        {
            if (state == ControllerState.Connected)
            {
                tcs.TrySetResult(true);
            }
            else if (state == ControllerState.Error || state == ControllerState.Disconnected)
            {
                failureReason = message;
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
                SetStatus($"Connected to {displayName}", Color.FromRgb(16, 185, 129));
                var sessionWin = new SessionWindow(client, displayName, $"{ip}:{port}");
                sessionWin.Show();
                SetStatus("Ready to connect", Color.FromRgb(16, 185, 129));
                return (true, null);
            }
            else
            {
                SetStatus("Connection failed", Color.FromRgb(239, 68, 68));
                client.Dispose();
                SetStatus("Ready to connect", Color.FromRgb(16, 185, 129));
                return (false, failureReason);
            }
        }
        catch (Exception ex)
        {
            client.StateChanged -= StateHandler;
            SetStatus($"Error: {ex.Message}", Color.FromRgb(239, 68, 68));
            client.Dispose();
            SetStatus("Ready to connect", Color.FromRgb(16, 185, 129));
            return (false, ex.Message);
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

    private static Task<bool> IsHostReachableAsync(string ip, int port, int timeoutMs = 2500)
        => LanDiscoveryClient.IsHostReachableAsync(ip, port, timeoutMs);

    private async void ConnectRemoteBtn_Click(object sender, RoutedEventArgs e)
    {
        string rawAddress = TargetIpTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(rawAddress))
        {
            MessageBox.Show("Please enter the remote PC's IP address or select a discovered device.", "Connection Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            TargetIpTextBox.Focus();
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

        string displayName = _discoveredAgents.FirstOrDefault(a => a.IpAddress.Equals(ip, StringComparison.OrdinalIgnoreCase))?.MachineName ?? ip;

        ConnectRemoteBtn.IsEnabled = false;
        SetStatus($"Checking connection to {ip}...", Color.FromRgb(59, 130, 246));

        try
        {
            // Reachability check: verify device is responding before asking for PIN
            bool isDiscovered = _discoveredAgents.Any(a => a.IpAddress.Equals(ip, StringComparison.OrdinalIgnoreCase) && a.Port == port);
            bool isReachable = isDiscovered || await IsHostReachableAsync(ip, port);

            if (!isReachable)
            {
                SetStatus("Host unreachable", Color.FromRgb(239, 68, 68));
                MessageBox.Show(
                    $"Unable to reach {ip}{(port != ProtocolConstants.DefaultPort ? $":{port}" : "")}.\n\nPlease ensure RemoteLAN is running on the target PC and both devices are connected to the same network.",
                    "Device Unreachable",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                SetStatus("Ready to connect", Color.FromRgb(16, 185, 129));
                return;
            }

            // If a saved password exists for this device, attempt 1-click connection
            if (_settingsManager.TryGetPassword(displayName, ip, out string savedPassword) && !string.IsNullOrWhiteSpace(savedPassword))
            {
                if (!isDiscovered)
                {
                    await Task.Delay(100);
                }

                SetStatus($"Connecting to {displayName} (using saved password)...", Color.FromRgb(59, 130, 246));
                var (connected, _) = await ConnectWithCredentialsAsync(ip, port, savedPassword, displayName);
                if (connected)
                {
                    return;
                }

                // If saved password failed (e.g. host changed its PIN), prompt for new PIN
                OpenPinModal(ip, port, displayName, fallbackFromFailedSavedPassword: true);
                return;
            }

            // Device is reachable and no saved password exists -> prompt for security PIN
            SetStatus("Ready to connect", Color.FromRgb(16, 185, 129));
            OpenPinModal(ip, port, displayName);
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