using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Net;
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
using WinForms = System.Windows.Forms;
using Color = System.Windows.Media.Color;

namespace RemoteLAN;

public partial class MainWindow : Window
{
    private readonly SettingsManager _settingsManager;
    private readonly AgentServer _server;
    private readonly LanDiscoveryClient _discoveryClient = new();
    private readonly ObservableCollection<DiscoveredAgent> _discoveredAgents = new();
    private readonly DispatcherTimer _discoveryTimer = new();
    private readonly HashSet<string> _localIpAddresses = new(StringComparer.OrdinalIgnoreCase) { "127.0.0.1", "localhost", "::1" };
    private WinForms.NotifyIcon? _trayIcon;
    private bool _isExplicitExit;
    private bool _hasShownTrayTip;
    private string? _modalTargetIp;
    private int _modalTargetPort;
    private string? _modalTargetDisplayName;
    private DiscoveredAgent? _modalTargetAgent;
    private string? _osModalTargetIp;
    private DiscoveredAgent? _osModalTargetAgent;
    private string? _osModalTargetDisplayName;
    private bool _isScanning;
    private IncomingConnectionEventArgs? _currentIncomingRequest;
    private ControllerClient? _pendingApprovalClient;
    private CancellationTokenSource? _pendingApprovalCts;
    private Views.HostChatWindow? _hostChatWindow;

    private bool _hasActiveNetwork = true;

    public sealed record DetectedInterfaceInfo(
        string Name,
        string Description,
        NetworkInterfaceType InterfaceType,
        OperationalStatus Status,
        bool HasIpv4Gateway,
        IReadOnlyList<string> Ipv4Addresses);

    public sealed class NetworkAddressItem
    {
        public string IpAddress { get; init; } = string.Empty;
        public string InterfaceName { get; init; } = string.Empty;
        public string InterfaceType { get; init; } = "Ethernet";
        public string DisplayText { get; init; } = string.Empty;
        public bool IsPrimary { get; init; }

        public bool IsWifi => InterfaceType.IndexOf("Wi-Fi", StringComparison.OrdinalIgnoreCase) >= 0 ||
                              InterfaceType.IndexOf("Wireless", StringComparison.OrdinalIgnoreCase) >= 0 ||
                              InterfaceName.IndexOf("Wi-Fi", StringComparison.OrdinalIgnoreCase) >= 0 ||
                              InterfaceName.IndexOf("Wireless", StringComparison.OrdinalIgnoreCase) >= 0;

        public bool IsLoopback => InterfaceType.IndexOf("Loopback", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                  IpAddress == "127.0.0.1";

        public bool IsOffline => InterfaceType.IndexOf("Offline", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                 IpAddress.IndexOf("Offline", StringComparison.OrdinalIgnoreCase) >= 0;

        public string BadgeBackgroundBrush =>
            IsOffline ? "#F1F5F9" :
            IsWifi ? "#F0FDF4" :
            IsLoopback ? "#F1F5F9" :
            "#EFF6FF";

        public string BadgeForegroundBrush =>
            IsOffline ? "#94A3B8" :
            IsWifi ? "#16A34A" :
            IsLoopback ? "#64748B" :
            "#2563EB";

        public string BadgeText =>
            IsOffline ? "Offline" :
            IsWifi ? "Wi-Fi" :
            IsLoopback ? "Local" :
            "Ethernet";

        public string IconData =>
            IsOffline
                ? "M 12,2 C 6.48,2 2,6.48 2,12 C 2,17.52 6.48,22 12,22 C 17.52,22 22,17.52 22,12 C 22,6.48 17.52,2 12,2 Z M 12,6 C 15.31,6 18,8.69 18,12 C 18,15.31 15.31,18 12,18 C 8.69,18 6,15.31 6,12 C 6,8.69 8.69,6 12,6 Z M 12,10 C 13.1,10 14,10.9 14,12 C 14,13.1 13.1,14 12,14 C 10.9,14 10,13.1 10,12 Z"
                : IsWifi
                    ? "M 12,18 A 1.5,1.5 0 1,1 12,21 A 1.5,1.5 0 1,1 12,18 Z M 7.05,14.05 C 9.78,11.32 14.22,11.32 16.95,14.05 L 18.36,12.64 C 14.85,9.13 9.15,9.13 5.64,12.64 Z M 3.51,10.51 C 8.2,5.82 15.8,5.82 20.49,10.51 L 21.9,9.1 C 16.43,3.63 7.57,3.63 2.1,9.1 Z"
                    : IsLoopback
                        ? "M 12,4 A 8,8 0 0,1 20,12 L 17,12 A 5,5 0 0,0 12,7 L 12,9.5 L 8,6 L 12,2.5 Z M 12,20 A 8,8 0 0,1 4,12 L 7,12 A 5,5 0 0,0 12,17 L 12,14.5 L 16,18 L 12,21.5 Z"
                        : "M 3,3 L 17,3 C 18.1,3 19,3.9 19,5 L 19,13 C 19,14.1 18.1,15 17,15 L 13,15 L 13,18 L 15,18 L 15,20 L 5,20 L 5,18 L 7,18 L 7,15 L 3,15 C 1.9,15 1,14.1 1,13 L 1,5 C 1,3.9 1.9,3 3,3 Z M 4,6 L 4,11 L 16,11 L 16,6 Z M 6,8 L 7.5,8 L 7.5,10 L 6,10 Z M 9.25,8 L 10.75,8 L 10.75,10 L 9.25,10 Z M 12.5,8 L 14,8 L 14,10 L 12.5,10 Z";

        public string IconBrush =>
            IsOffline ? "#94A3B8" :
            IsWifi ? "#16A34A" :
            IsLoopback ? "#64748B" :
            "#2563EB";

        public override string ToString() => DisplayText;
    }

    public static (List<NetworkAddressItem> Items, bool HasActiveNetwork) EvaluateNetworkInterfaces(
        IEnumerable<DetectedInterfaceInfo> interfaces)
    {
        var rawItems = new List<(NetworkAddressItem Item, int Priority)>();

        foreach (var ni in interfaces)
        {
            if (ni.Status != OperationalStatus.Up) continue;
            if (ni.InterfaceType == NetworkInterfaceType.Loopback) continue;

            bool isWifi = ni.InterfaceType == NetworkInterfaceType.Wireless80211 ||
                          ni.Name.IndexOf("wi-fi", StringComparison.OrdinalIgnoreCase) >= 0 ||
                          ni.Name.IndexOf("wireless", StringComparison.OrdinalIgnoreCase) >= 0 ||
                          ni.Description.IndexOf("wi-fi", StringComparison.OrdinalIgnoreCase) >= 0 ||
                          ni.Description.IndexOf("wireless", StringComparison.OrdinalIgnoreCase) >= 0;

            string ifType = isWifi ? "Wi-Fi" : "Ethernet";

            // Priority:
            // 0: Ethernet with Gateway
            // 1: Ethernet without Gateway
            // 2: Wi-Fi with Gateway
            // 3: Wi-Fi without Gateway
            int priority = !isWifi
                ? (ni.HasIpv4Gateway ? 0 : 1)
                : (ni.HasIpv4Gateway ? 2 : 3);

            foreach (var ip in ni.Ipv4Addresses)
            {
                if (string.IsNullOrWhiteSpace(ip) || ip.StartsWith("169.254.") || ip == "0.0.0.0" || ip == "127.0.0.1")
                {
                    continue;
                }

                rawItems.Add((new NetworkAddressItem
                {
                    IpAddress = ip,
                    InterfaceName = ni.Name,
                    InterfaceType = ifType,
                    DisplayText = $"{ip} ({ni.Name})",
                    IsPrimary = false
                }, priority));
            }
        }

        if (rawItems.Count == 0)
        {
            return (new List<NetworkAddressItem>(), false);
        }

        var sorted = rawItems
            .OrderBy(r => r.Priority)
            .ThenBy(r => r.Item.IpAddress, StringComparer.OrdinalIgnoreCase)
            .Select(r => r.Item)
            .ToList();

        var primary = sorted[0];
        sorted[0] = new NetworkAddressItem
        {
            IpAddress = primary.IpAddress,
            InterfaceName = primary.InterfaceName,
            InterfaceType = primary.InterfaceType,
            DisplayText = primary.DisplayText,
            IsPrimary = true
        };

        return (sorted, true);
    }

    public MainWindow() : this(new SettingsManager(), null)
    {
    }

    public MainWindow(SettingsManager settings, AgentServer? existingServer = null)
    {
        _settingsManager = settings ?? new SettingsManager();
        InitializeComponent();

        HostDeviceNameText.Text = Environment.MachineName;

        if (existingServer != null)
        {
            _server = existingServer;
        }
        else
        {
            // Load persistent host PIN and unattended access credentials
            string? savedHostPin = _settingsManager.GetHostPin();
            bool unattendedEnabled = _settingsManager.IsUnattendedAccessEnabled();
            string? unattendedPassword = _settingsManager.GetUnattendedPassword();

            _server = new AgentServer(
                ProtocolConstants.DefaultPort,
                initialPin: savedHostPin,
                unattendedAccessEnabled: unattendedEnabled,
                unattendedPassword: unattendedPassword,
                settingsManager: _settingsManager);

            // If this is the first run and a PIN was newly generated, persist it
            if (string.IsNullOrWhiteSpace(savedHostPin))
            {
                _settingsManager.SaveHostPin(_server.PinManager.CurrentPin);
            }

            int rotationMinutes = _settingsManager.PinRotationIntervalMinutes;
            if (rotationMinutes > 0)
            {
                _server.PinManager.SetRotationInterval(rotationMinutes);
            }
        }

        _server.StatusChanged += Server_StatusChanged;
        _server.ClientConnected += Server_ClientConnected;
        _server.ClientDisconnected += Server_ClientDisconnected;
        _server.IncomingConnectionRequested += Server_IncomingConnectionRequested;
        _server.IncomingConnectionDismissed += Server_IncomingConnectionDismissed;
        _server.PinManager.PinChanged += PinManager_PinChanged;

        int configuredRotation = _settingsManager.PinRotationIntervalMinutes;
        if (configuredRotation > 0)
        {
            _server.PinManager.SetRotationInterval(configuredRotation);
        }

        UpdatePinDisplay(_server.PinManager.CurrentPin);
        LoadLocalIpAddresses();

        _server.Start();
        if (existingServer == null)
        {
            DiagnosticLogger.Log($"[MainWindow] AgentServer started on port {_server.Port}. Host: '{Environment.MachineName}'");
        }
        else
        {
            DiagnosticLogger.Log($"[MainWindow] Attached to existing AgentServer running on port {_server.Port}. Host: '{Environment.MachineName}'");
        }

        // Show elevation banner if running under standard user integrity
        ElevationBanner.Visibility = DesktopManager.IsAdministrator ? Visibility.Collapsed : Visibility.Visible;

        // Bind discovered PCs collection to the AnyDesk-style grid
        DiscoveredPcsListBox.ItemsSource = _discoveredAgents;

        // Pre-populate discovered agents grid with saved device history
        var savedHistory = _settingsManager.GetDeviceHistory();
        foreach (var dev in savedHistory)
        {
            var historyAgent = new DiscoveredAgent
            {
                MachineId = string.IsNullOrEmpty(dev.MachineId) ? dev.MachineName : dev.MachineId,
                MachineName = dev.MachineName,
                Port = dev.Port > 0 ? dev.Port : ProtocolConstants.DefaultPort,
                Version = dev.Version,
                IsOnline = false,
                LastSeen = dev.LastSeenUtc
            };
            historyAgent.AddOrUpdateEndpoint(dev.IpAddress, string.IsNullOrEmpty(dev.InterfaceType) ? "Ethernet" : dev.InterfaceType, dev.LastSeenUtc);
            _discoveredAgents.Add(historyAgent);
        }

        UpdateEmptyState();
        UpdateDiscoveredCount();

        // Setup continuous automatic LAN discovery (runs every 10 seconds)
        _discoveryTimer.Interval = TimeSpan.FromSeconds(10);
        _discoveryTimer.Tick += async (s, e) => await PerformDiscoveryScanAsync();
        _discoveryTimer.Start();

        // Trigger immediate scan upon application launch
        _ = PerformDiscoveryScanAsync();

        // Initialize system tray notification icon
        InitializeTrayIcon();

        // Listen for network connectivity and address changes
        NetworkChange.NetworkAddressChanged += NetworkChange_NetworkAddressChanged;
        NetworkChange.NetworkAvailabilityChanged += NetworkChange_NetworkAvailabilityChanged;
    }

    private void NetworkChange_NetworkAddressChanged(object? sender, EventArgs e)
    {
        Dispatcher.InvokeAsync(LoadLocalIpAddresses);
    }

    private void NetworkChange_NetworkAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e)
    {
        Dispatcher.InvokeAsync(LoadLocalIpAddresses);
    }

    private void LoadLocalIpAddresses()
    {
        var detectedList = new List<DetectedInterfaceInfo>();

        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up) continue;
            if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

            var ipProps = ni.GetIPProperties();
            bool hasGateway = ipProps.GatewayAddresses.Any(g =>
                g.Address.AddressFamily == AddressFamily.InterNetwork &&
                !g.Address.Equals(IPAddress.Any) &&
                !g.Address.Equals(IPAddress.None));

            var ips = new List<string>();
            foreach (var unicast in ipProps.UnicastAddresses)
            {
                if (unicast.Address.AddressFamily == AddressFamily.InterNetwork)
                {
                    ips.Add(unicast.Address.ToString());
                }
            }

            detectedList.Add(new DetectedInterfaceInfo(
                ni.Name,
                ni.Description,
                ni.NetworkInterfaceType,
                ni.OperationalStatus,
                hasGateway,
                ips));
        }

        var (items, hasActive) = EvaluateNetworkInterfaces(detectedList);

        _localIpAddresses.Clear();
        _localIpAddresses.Add("127.0.0.1");
        _localIpAddresses.Add("localhost");
        _localIpAddresses.Add("::1");

        foreach (var item in items)
        {
            _localIpAddresses.Add(item.IpAddress);
        }

        if (hasActive && items.Count > 0)
        {
            LocalIpsComboBox.ItemsSource = items;
            LocalIpsComboBox.SelectedIndex = 0;
            LocalIpsComboBox.IsEnabled = true;
            SetNetworkConnectedState(true);
        }
        else
        {
            LocalIpsComboBox.ItemsSource = new List<NetworkAddressItem>
            {
                new()
                {
                    IpAddress = "Offline",
                    InterfaceName = "No active connection",
                    InterfaceType = "Offline",
                    DisplayText = "No active connection",
                    IsPrimary = false
                }
            };
            LocalIpsComboBox.SelectedIndex = 0;
            LocalIpsComboBox.IsEnabled = false;
            SetNetworkConnectedState(false);
        }
    }

    private void SetNetworkConnectedState(bool isConnected)
    {
        _hasActiveNetwork = isConnected;

        if (isConnected)
        {
            UpdatePinDisplay(_server.PinManager.CurrentPin);
            CopyPinBtn.IsEnabled = true;
            RegeneratePinBtn.IsEnabled = true;
            CustomPinBtn.IsEnabled = true;
            CopyIpBtn.IsEnabled = true;
            ConnectRemoteBtn.IsEnabled = true;
            LocalIpsComboBox.IsEnabled = true;
            SetStatus("Ready to connect", Color.FromRgb(16, 185, 129));
            DiscoveredDevicesCountText.Text = "Scanning local network...";
        }
        else
        {
            PinTextBlock.Text = "------";
            PinTextBlock.Foreground = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromInvariantString("#94A3B8")!;
            CopyPinBtn.IsEnabled = false;
            RegeneratePinBtn.IsEnabled = false;
            CustomPinBtn.IsEnabled = false;
            CopyIpBtn.IsEnabled = false;
            ConnectRemoteBtn.IsEnabled = false;
            LocalIpsComboBox.IsEnabled = false;
            SetStatus("No active network connection", Color.FromRgb(239, 68, 68));
            DiscoveredDevicesCountText.Text = "Offline";
        }
    }

    private void UpdatePinDisplay(string pin)
    {
        Dispatcher.Invoke(() =>
        {
            if (_hasActiveNetwork)
            {
                PinTextBlock.Text = pin;
                PinTextBlock.Foreground = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromInvariantString("#0284C7")!;
            }
            else
            {
                PinTextBlock.Text = "------";
                PinTextBlock.Foreground = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromInvariantString("#94A3B8")!;
            }
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

    public void ShowAndActivate()
    {
        Dispatcher.Invoke(() =>
        {
            if (!IsVisible)
            {
                Show();
            }

            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
            }

            Activate();
            Topmost = true;
            Topmost = false;
            Focus();
        });
    }

    private void Server_ClientConnected(string endpoint)
    {
        Dispatcher.Invoke(() =>
        {
            bool isLocked = DesktopManager.IsLockScreenActive();
            if (!isLocked)
            {
                ShowAndActivate();
            }

            try
            {
                _trayIcon?.ShowBalloonTip(3000, "RemoteLAN Connection", $"Incoming remote control session from {endpoint}", WinForms.ToolTipIcon.Info);
            }
            catch { }

            ActiveClientCard.Visibility = Visibility.Visible;
            ActiveClientEndpointText.Text = endpoint;
            SetStatus($"Connected: viewer from {endpoint}", Color.FromRgb(59, 130, 246)); // Blue

            if (!isLocked && !endpoint.StartsWith("[WebBridge]", StringComparison.OrdinalIgnoreCase))
            {
                // Launch the floating host chat widget only for native desktop sessions that support chat
                try
                {
                    _hostChatWindow?.Close();
                }
                catch { }
                try
                {
                    _hostChatWindow = new Views.HostChatWindow(_server, endpoint);
                    _hostChatWindow.Show();
                }
                catch { }
            }
        });
    }

    private void Server_ClientDisconnected()
    {
        Dispatcher.Invoke(() =>
        {
            ActiveClientCard.Visibility = Visibility.Collapsed;
            SetStatus("Ready to connect", Color.FromRgb(16, 185, 129)); // Green
            try
            {
                _hostChatWindow?.Close();
            }
            catch { }
            _hostChatWindow = null;
        });
    }

    public void HandleIncomingConnectionRequest(IncomingConnectionEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            _currentIncomingRequest = e;
            IncomingRequesterNameText.Text = e.ClientMachineName;
            IncomingRequesterEndpointText.Text = e.ClientIp;
            IncomingRequestOverlay.Visibility = Visibility.Visible;
            ShowAndActivate();
            try { System.Media.SystemSounds.Asterisk.Play(); } catch { }
            try { _trayIcon?.ShowBalloonTip(5000, "Incoming Remote Connection", $"{e.ClientMachineName} ({e.ClientIp}) is requesting access.", WinForms.ToolTipIcon.Info); } catch { }
        });
    }

    private void Server_IncomingConnectionRequested(IncomingConnectionEventArgs e)
    {
        HandleIncomingConnectionRequest(e);
    }

    private void Server_IncomingConnectionDismissed()
    {
        Dispatcher.Invoke(() =>
        {
            _currentIncomingRequest = null;
            IncomingRequestOverlay.Visibility = Visibility.Collapsed;
        });
    }

    private void AcceptIncomingBtn_Click(object sender, RoutedEventArgs e)
    {
        var req = _currentIncomingRequest;
        _currentIncomingRequest = null;
        IncomingRequestOverlay.Visibility = Visibility.Collapsed;
        req?.Accept();
    }

    private void RejectIncomingBtn_Click(object sender, RoutedEventArgs e)
    {
        var req = _currentIncomingRequest;
        _currentIncomingRequest = null;
        IncomingRequestOverlay.Visibility = Visibility.Collapsed;
        req?.Reject();
    }

    private async void CopyIp_Click(object sender, RoutedEventArgs e)
    {
        if (!_hasActiveNetwork) return;

        if (LocalIpsComboBox.SelectedItem is NetworkAddressItem item && !item.IsOffline)
        {
            try
            {
                Clipboard.SetText(item.IpAddress);
                if (sender is Button btn)
                {
                    string oldText = btn.Content?.ToString() ?? "Copy";
                    btn.Content = "Copied!";
                    btn.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x16, 0xA3, 0x4A));
                    await Task.Delay(1200);
                    btn.Content = oldText;
                    btn.ClearValue(Button.ForegroundProperty);
                }
            }
            catch { }
        }
    }

    private async void CopyPin_Click(object sender, RoutedEventArgs e)
    {
        if (!_hasActiveNetwork) return;

        try
        {
            Clipboard.SetText(_server.PinManager.CurrentPin);
            if (sender is Button btn)
            {
                string oldText = btn.Content?.ToString() ?? "Copy";
                btn.Content = "Copied!";
                btn.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x16, 0xA3, 0x4A));
                await Task.Delay(1200);
                btn.Content = oldText;
                btn.ClearValue(Button.ForegroundProperty);
            }
        }
        catch { }
    }

    private void RegeneratePin_Click(object sender, RoutedEventArgs e)
    {
        if (!_hasActiveNetwork) return;
        _server.PinManager.RegeneratePin();
    }

    private void CustomPin_Click(object sender, RoutedEventArgs e)
    {
        if (!_hasActiveNetwork) return;
        CustomCodeTextBox.Text = _server.PinManager.CurrentPin;
        CustomCodeModalStatusText.Visibility = Visibility.Collapsed;
        CustomCodeModalStatusText.Text = string.Empty;
        CustomCodeModalOverlay.Visibility = Visibility.Visible;
        CustomCodeTextBox.Focus();
        CustomCodeTextBox.SelectAll();
    }

    private void CloseCustomCodeModal_Click(object sender, RoutedEventArgs e)
    {
        CustomCodeModalOverlay.Visibility = Visibility.Collapsed;
    }

    private void CustomCodeModalOverlay_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource == CustomCodeModalOverlay)
        {
            CloseCustomCodeModal_Click(this, new RoutedEventArgs());
        }
    }

    private void CustomCodeTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            SaveCustomCode_Click(this, new RoutedEventArgs());
        }
        else if (e.Key == Key.Escape)
        {
            CloseCustomCodeModal_Click(this, new RoutedEventArgs());
        }
    }

    private void SaveCustomCode_Click(object sender, RoutedEventArgs e)
    {
        string code = CustomCodeTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(code) || code.Length < 4)
        {
            CustomCodeModalStatusText.Text = "Code must be at least 4 alphanumeric characters.";
            CustomCodeModalStatusText.Visibility = Visibility.Visible;
            CustomCodeTextBox.Focus();
            return;
        }

        _server.PinManager.SetPin(code);
        CustomCodeModalOverlay.Visibility = Visibility.Collapsed;
    }

    private void DisconnectIncomingClient_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _hostChatWindow?.Close();
        }
        catch { }
        _hostChatWindow = null;
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

            // Double filter against local machine name, local machine ID, loopback, and local IPs
            string localMachineId = AgentIdentity.GetOrCreateMachineId();
            var filteredAgents = rawAgents.Where(a =>
                !string.Equals(a.MachineName, Environment.MachineName, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(a.MachineId, localMachineId, StringComparison.OrdinalIgnoreCase) &&
                !_localIpAddresses.Contains(a.IpAddress) &&
                a.IpAddress != "127.0.0.1"
            ).ToList();

            Dispatcher.Invoke(() =>
            {
                // Mark devices that did not respond to this scan as offline (do NOT remove from history!)
                foreach (var agent in _discoveredAgents)
                {
                    bool stillOnline = filteredAgents.Any(a =>
                        string.Equals(a.MachineId, agent.MachineId, StringComparison.OrdinalIgnoreCase));
                    if (!stillOnline)
                    {
                        if ((DateTime.UtcNow - agent.LastSeen).TotalSeconds > 15)
                        {
                            agent.IsOnline = false;
                        }
                    }
                }

                // Add newly discovered devices or update existing to online
                foreach (var agent in filteredAgents)
                {
                    var existing = _discoveredAgents.FirstOrDefault(a =>
                        string.Equals(a.MachineId, agent.MachineId, StringComparison.OrdinalIgnoreCase));

                    if (existing == null)
                    {
                        agent.IsOnline = true;
                        agent.LastSeen = DateTime.UtcNow;
                        _discoveredAgents.Add(agent);
                        _settingsManager.UpdateDeviceInHistory(agent);
                    }
                    else
                    {
                        existing.IsOnline = true;
                        existing.LastSeen = DateTime.UtcNow;
                        existing.MachineName = agent.MachineName;
                        existing.Version = agent.Version;
                        existing.Port = agent.Port;

                        foreach (var ep in agent.Endpoints)
                        {
                            existing.AddOrUpdateEndpoint(ep.IpAddress, ep.InterfaceType, ep.LastSeen);
                        }

                        // Save or update in persistent device history
                        _settingsManager.UpdateDeviceInHistory(existing);
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
        int onlineCount = _discoveredAgents.Count(a => a.IsOnline);
        int totalCount = _discoveredAgents.Count;

        if (totalCount == 0)
        {
            DiscoveredDevicesCountText.Text = "Scanning local network...";
        }
        else if (onlineCount == totalCount)
        {
            DiscoveredDevicesCountText.Text = totalCount == 1
                ? "1 remote device online"
                : $"{totalCount} remote devices online";
        }
        else
        {
            DiscoveredDevicesCountText.Text = $"{onlineCount} online • {totalCount} in history";
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

    private void CardKebab_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is Button btn && btn.ContextMenu != null)
        {
            btn.ContextMenu.PlacementTarget = btn;
            btn.ContextMenu.IsOpen = true;
        }
    }

    private async void CardMenuConnect_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement elem && elem.DataContext is DiscoveredAgent agent)
        {
            await HandleAgentCardClickAsync(agent);
        }
    }

    private void CardMenuCopyIp_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement elem && elem.DataContext is DiscoveredAgent agent)
        {
            try
            {
                Clipboard.SetText(agent.IpAddress);
                SetStatus($"Copied IP ({agent.IpAddress}) to clipboard", Color.FromRgb(16, 185, 129));
            }
            catch { }
        }
    }

    private void CardMenuForgetPassword_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement elem && elem.DataContext is DiscoveredAgent agent)
        {
            _settingsManager.RemovePassword(agent.MachineName, agent.IpAddress);
            SetStatus($"Removed saved PIN for {agent.MachineName}", Color.FromRgb(100, 116, 139));
        }
    }

    private void CardMenuConfigureOsPassword_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement elem && elem.DataContext is DiscoveredAgent agent)
        {
            OpenOsPasswordModal(agent.IpAddress, agent.MachineName, agent);
        }
    }

    private void CardMenuForgetOsPassword_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement elem && elem.DataContext is DiscoveredAgent agent)
        {
            _settingsManager.RemoveOsPassword(agent.MachineId, agent.MachineName, agent.IpAddress);
            SetStatus($"Removed saved OS password for {agent.MachineName}", Color.FromRgb(100, 116, 139));
        }
    }

    private void CardMenuRemoveFromHistory_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement elem && elem.DataContext is DiscoveredAgent agent)
        {
            _discoveredAgents.Remove(agent);
            _settingsManager.RemoveDeviceFromHistory(agent.MachineName, agent.IpAddress, agent.Port, agent.MachineId);
            UpdateEmptyState();
            UpdateDiscoveredCount();
            SetStatus($"Removed {agent.MachineName} from history", Color.FromRgb(100, 116, 139));
        }
    }

    private async Task HandleAgentCardClickAsync(DiscoveredAgent agent)
    {
        // Check if we already have a saved password for this device
        if (_settingsManager.TryGetPassword(agent.MachineName, agent.IpAddress, out string savedPassword) && !string.IsNullOrWhiteSpace(savedPassword))
        {
            SetStatus($"Connecting to {agent.MachineName} (using saved password)...", Color.FromRgb(59, 130, 246));
            var (success, _) = await ConnectWithCredentialsAsync(agent.IpAddress, agent.Port, savedPassword, agent.MachineName, agent.MachineId);

            if (!success)
            {
                // Saved password failed (e.g. host changed its PIN) -> open PIN modal prompting for new PIN
                OpenPinModal(agent.IpAddress, agent.Port, agent.MachineName, fallbackFromFailedSavedPassword: true, agent: agent);
            }
        }
        else
        {
            OpenPinModal(agent.IpAddress, agent.Port, agent.MachineName, agent: agent);
        }
    }

    private void CancelPendingApprovalConnection()
    {
        try
        {
            _pendingApprovalCts?.Cancel();
            _pendingApprovalCts?.Dispose();
            _pendingApprovalCts = null;

            if (_pendingApprovalClient != null)
            {
                _pendingApprovalClient.Disconnect();
                _pendingApprovalClient.Dispose();
                _pendingApprovalClient = null;
            }
        }
        catch { }
    }

    private void StartPendingApprovalConnection(string ip, int port, string displayName)
    {
        CancelPendingApprovalConnection();

        _pendingApprovalCts = new CancellationTokenSource();
        var ct = _pendingApprovalCts.Token;
        var client = new ControllerClient();
        _pendingApprovalClient = client;

        client.StateChanged += (state, message) =>
        {
            Dispatcher.Invoke(() =>
            {
                if (state == ControllerState.Connected)
                {
                    // Remote user accepted incoming connection!
                    if (PinModalOverlay.Visibility == Visibility.Visible && _modalTargetIp == ip)
                    {
                        string? modalMachineId = _modalTargetAgent?.MachineId;
                        string osPass = ModalOsPasswordBox.Password;
                        if (!string.IsNullOrEmpty(osPass))
                        {
                            if (SaveModalOsPasswordCheckBox.IsChecked == true)
                            {
                                _settingsManager.SaveOsPassword(modalMachineId, displayName, ip, osPass);
                            }
                            else
                            {
                                _settingsManager.RemoveOsPassword(modalMachineId, displayName, ip);
                            }
                        }

                        PinModalOverlay.Visibility = Visibility.Collapsed;
                        _modalTargetIp = null;
                        _modalTargetDisplayName = null;
                        _pendingApprovalClient = null;

                        SetStatus($"Connected to {displayName}", Color.FromRgb(16, 185, 129));
                        var sessionWin = new SessionWindow(client, displayName, $"{ip}:{port}", _settingsManager, ip, modalMachineId);
                        sessionWin.Show();
                        SetStatus("Ready to connect", Color.FromRgb(16, 185, 129));
                    }
                }
                else if (state == ControllerState.Error)
                {
                    if (PinModalOverlay.Visibility == Visibility.Visible && _modalTargetIp == ip)
                    {
                        if (message.Contains("sign-in", StringComparison.OrdinalIgnoreCase) || message.Contains("lock", StringComparison.OrdinalIgnoreCase))
                        {
                            ModalStatusText.Text = "Remote host is at Windows sign-in screen. Enter PIN or unattended password to connect.";
                        }
                        else if (message.Contains("declined", StringComparison.OrdinalIgnoreCase))
                        {
                            ModalStatusText.Text = "Connection was declined by the remote user.";
                        }
                        else if (message.Contains("timed out", StringComparison.OrdinalIgnoreCase))
                        {
                            ModalStatusText.Text = "Connection request timed out. Enter PIN to connect.";
                        }
                        ModalStatusText.Visibility = Visibility.Visible;
                        ModalConnectBtn.IsEnabled = true;
                    }
                }
            });
        };

        _ = Task.Run(async () =>
        {
            try
            {
                await client.ConnectAsync(ip, port, pin: string.Empty, ct).ConfigureAwait(false);
            }
            catch { }
        }, ct);
    }

    private void OpenPinModal(string ip, int port, string displayName, bool fallbackFromFailedSavedPassword = false, DiscoveredAgent? agent = null)
    {
        _modalTargetAgent = agent;
        _modalTargetIp = ip;
        _modalTargetPort = port;
        _modalTargetDisplayName = displayName;

        ModalDeviceNameText.Text = displayName;
        ModalPinTextBox.Text = string.Empty;
        ModalConnectBtn.IsEnabled = true;

        if (agent != null && agent.Endpoints.Count > 1)
        {
            ModalSingleIpPanel.Visibility = Visibility.Collapsed;
            ModalEndpointsComboBox.Visibility = Visibility.Visible;
            ModalEndpointsComboBox.ItemsSource = agent.Endpoints;
            ModalEndpointsComboBox.SelectedItem = agent.SelectedEndpoint ?? agent.Endpoints.FirstOrDefault();
        }
        else
        {
            ModalEndpointsComboBox.Visibility = Visibility.Collapsed;
            ModalSingleIpPanel.Visibility = Visibility.Visible;
            ModalDeviceIpText.Text = port == ProtocolConstants.DefaultPort ? ip : $"{ip}:{port}";
            ModalInterfaceBadgeText.Text = agent?.InterfaceType ?? "Ethernet";
            try
            {
                ModalInterfaceBadge.Background = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromInvariantString(agent?.SelectedEndpoint?.BadgeBackgroundBrush ?? "#EFF6FF")!;
                ModalInterfaceBadgeText.Foreground = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromInvariantString(agent?.SelectedEndpoint?.BadgeForegroundBrush ?? "#2563EB")!;
            }
            catch { }
        }

        bool hasSaved = _settingsManager.HasSavedPassword(displayName, ip);
        ModalForgetPasswordBtn.Visibility = hasSaved ? Visibility.Visible : Visibility.Collapsed;

        bool hasSavedOs = _settingsManager.HasSavedOsPassword(agent?.MachineId, displayName, ip);
        ModalForgetOsPasswordBtn.Visibility = hasSavedOs ? Visibility.Visible : Visibility.Collapsed;

        if (_settingsManager.TryGetOsPassword(agent?.MachineId, displayName, ip, out string? existingOsPass))
        {
            ModalOsPasswordBox.Password = existingOsPass;
            SaveModalOsPasswordCheckBox.IsChecked = true;
        }
        else
        {
            ModalOsPasswordBox.Password = string.Empty;
            SaveModalOsPasswordCheckBox.IsChecked = true;
        }

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

        // Connect in background to request remote user approval
        StartPendingApprovalConnection(ip, port, displayName);
    }

    private void ModalEndpointsComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ModalEndpointsComboBox.SelectedItem is AgentEndpointInfo endpoint && _modalTargetAgent != null)
        {
            _modalTargetAgent.SelectedEndpoint = endpoint;
            _modalTargetIp = endpoint.IpAddress;

            bool hasSaved = _settingsManager.HasSavedPassword(_modalTargetDisplayName, _modalTargetIp);
            ModalForgetPasswordBtn.Visibility = hasSaved ? Visibility.Visible : Visibility.Collapsed;

            string? machineId = _modalTargetAgent?.MachineId;
            bool hasSavedOs = _settingsManager.HasSavedOsPassword(machineId, _modalTargetDisplayName, _modalTargetIp);
            ModalForgetOsPasswordBtn.Visibility = hasSavedOs ? Visibility.Visible : Visibility.Collapsed;

            if (_settingsManager.TryGetOsPassword(machineId, _modalTargetDisplayName, _modalTargetIp, out string? existingOsPass))
            {
                ModalOsPasswordBox.Password = existingOsPass;
            }
            else
            {
                ModalOsPasswordBox.Password = string.Empty;
            }

            if (!string.IsNullOrEmpty(_modalTargetDisplayName))
            {
                StartPendingApprovalConnection(_modalTargetIp, _modalTargetPort, _modalTargetDisplayName);
            }
        }
    }

    private void ClosePinModal_Click(object sender, RoutedEventArgs e)
    {
        CancelPendingApprovalConnection();
        PinModalOverlay.Visibility = Visibility.Collapsed;
        _modalTargetIp = null;
        _modalTargetDisplayName = null;
        _modalTargetAgent = null;
    }

    private void ModalForgetPasswordBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_modalTargetIp))
        {
            _settingsManager.RemovePassword(_modalTargetDisplayName, _modalTargetIp);
            ModalForgetPasswordBtn.Visibility = Visibility.Collapsed;
            ModalStatusText.Text = "Saved connection code removed.";
            ModalStatusText.Visibility = Visibility.Visible;
        }
    }

    private void ModalForgetOsPasswordBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_modalTargetIp))
        {
            _settingsManager.RemoveOsPassword(_modalTargetAgent?.MachineId, _modalTargetDisplayName, _modalTargetIp);
            ModalOsPasswordBox.Password = string.Empty;
            ModalForgetOsPasswordBtn.Visibility = Visibility.Collapsed;
            ModalStatusText.Text = "Saved OS password removed.";
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
        if (e.Key == Key.Escape)
        {
            if (IncomingRequestOverlay.Visibility == Visibility.Visible)
            {
                RejectIncomingBtn_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
            else if (PinModalOverlay.Visibility == Visibility.Visible)
            {
                ClosePinModal_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
            else if (CustomCodeModalOverlay.Visibility == Visibility.Visible)
            {
                CloseCustomCodeModal_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
            else if (OsPasswordModalOverlay.Visibility == Visibility.Visible)
            {
                CloseOsPasswordModal_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
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

    private void ModalOsPasswordBox_KeyDown(object sender, KeyEventArgs e)
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
            ModalStatusText.Text = "Please enter the access code or unattended password.";
            ModalStatusText.Visibility = Visibility.Visible;
            ModalPinTextBox.Focus();
            return;
        }

        // Cancel pending background approval connection
        CancelPendingApprovalConnection();

        ModalConnectBtn.IsEnabled = false;
        ModalStatusText.Visibility = Visibility.Collapsed;

        string ip = _modalTargetIp;
        int port = _modalTargetPort;
        string displayName = _modalTargetDisplayName ?? ip;

        string? modalMachineId = _modalTargetAgent?.MachineId;
        bool remember = SaveModalPasswordCheckBox.IsChecked == true;
        var (connected, errorMsg) = await ConnectWithCredentialsAsync(ip, port, pin, displayName, modalMachineId);

        if (connected)
        {
            if (remember)
            {
                _settingsManager.SavePassword(displayName, ip, pin);
            }

            string osPass = ModalOsPasswordBox.Password;
            if (!string.IsNullOrEmpty(osPass))
            {
                if (SaveModalOsPasswordCheckBox.IsChecked == true)
                {
                    _settingsManager.SaveOsPassword(modalMachineId, displayName, ip, osPass);
                }
                else
                {
                    _settingsManager.RemoveOsPassword(modalMachineId, displayName, ip);
                }
            }

            PinModalOverlay.Visibility = Visibility.Collapsed;
            _modalTargetIp = null;
            _modalTargetDisplayName = null;
            _modalTargetAgent = null;
        }
        else
        {
            if (errorMsg != null && (errorMsg.Contains("sign-in", StringComparison.OrdinalIgnoreCase) || errorMsg.Contains("lock", StringComparison.OrdinalIgnoreCase)))
            {
                ModalStatusText.Text = "Remote host is at Windows sign-in screen. Enter PIN or unattended password to connect.";
            }
            else if (errorMsg != null && errorMsg.Contains("Authentication", StringComparison.OrdinalIgnoreCase))
            {
                ModalStatusText.Text = "Incorrect access code or password. Please verify the credentials on the remote PC.";
            }
            else
            {
                ModalStatusText.Text = "Could not connect. Check credentials or ensure RemoteLAN is running.";
            }
            ModalStatusText.Visibility = Visibility.Visible;
            ModalConnectBtn.IsEnabled = true;
            ModalPinTextBox.Focus();
            ModalPinTextBox.SelectAll();
        }
    }

    private async Task<(bool Success, string? ErrorMessage)> ConnectWithCredentialsAsync(string ip, int port, string pin, string displayName, string? machineId = null)
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
                var sessionWin = new SessionWindow(client, displayName, $"{ip}:{port}", _settingsManager, ip, machineId);
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
        if (!_hasActiveNetwork)
        {
            MessageBox.Show("No active network connection detected. Please connect to a LAN or Wi-Fi network before initiating a connection.", "Offline", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

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

    private void InitializeTrayIcon()
    {
        try
        {
            System.Drawing.Icon appIcon;
            try
            {
                var streamInfo = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/icon.ico"));
                if (streamInfo != null)
                {
                    using var stream = streamInfo.Stream;
                    appIcon = new System.Drawing.Icon(stream);
                }
                else
                {
                    string iconPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "icon.ico");
                    appIcon = System.IO.File.Exists(iconPath) ? new System.Drawing.Icon(iconPath) : System.Drawing.SystemIcons.Application;
                }
            }
            catch
            {
                appIcon = System.Drawing.SystemIcons.Application;
            }

            _trayIcon = new WinForms.NotifyIcon
            {
                Icon = appIcon,
                Text = "RemoteLAN — LAN Remote Desktop",
                Visible = true
            };

            var contextMenu = new WinForms.ContextMenuStrip();

            var openItem = new WinForms.ToolStripMenuItem("Open RemoteLAN");
            openItem.Font = new System.Drawing.Font(openItem.Font, System.Drawing.FontStyle.Bold);
            openItem.Click += (s, e) => ShowAndActivate();

            var connectPhoneItem = new WinForms.ToolStripMenuItem("📱 Connect Phone (QR)...");
            connectPhoneItem.Click += (s, e) => Dispatcher.Invoke(() =>
            {
                ShowAndActivate();
                ConnectPhone_Click(this, new RoutedEventArgs());
            });

            var settingsItem = new WinForms.ToolStripMenuItem("Settings...");
            settingsItem.Click += (s, e) => Dispatcher.Invoke(() => OpenSettingsWindow(0));

            var aboutItem = new WinForms.ToolStripMenuItem("About RemoteLAN");
            aboutItem.Click += (s, e) => Dispatcher.Invoke(() => OpenSettingsWindow(3));

            var hostItem = new WinForms.ToolStripMenuItem($"Host: {Environment.MachineName}");
            hostItem.Enabled = false;

            var exitItem = new WinForms.ToolStripMenuItem("Exit RemoteLAN");
            exitItem.Click += (s, e) => ExitApplication();

            contextMenu.Items.Add(openItem);
            contextMenu.Items.Add(connectPhoneItem);
            contextMenu.Items.Add(settingsItem);
            contextMenu.Items.Add(aboutItem);
            contextMenu.Items.Add(new WinForms.ToolStripSeparator());
            contextMenu.Items.Add(hostItem);
            contextMenu.Items.Add(new WinForms.ToolStripSeparator());
            contextMenu.Items.Add(exitItem);

            _trayIcon.ContextMenuStrip = contextMenu;

            _trayIcon.DoubleClick += (s, e) => ShowAndActivate();
            _trayIcon.Click += (s, e) =>
            {
                if (e is WinForms.MouseEventArgs mouseArgs && mouseArgs.Button == WinForms.MouseButtons.Left)
                {
                    ShowAndActivate();
                }
            };
        }
        catch (Exception ex)
        {
            DiagnosticLogger.LogException("InitializeTrayIcon (Explorer or shell unavailable)", ex);
        }
    }

    private void ConnectPhone_Click(object sender, RoutedEventArgs e)
    {
        var qrWin = new WebBridgeQrWindow(_settingsManager, _server.PinManager, _server, () => OpenSettingsWindow(1))
        {
            Owner = this
        };
        qrWin.ShowDialog();
    }

    private void OpenSettings_Click(object sender, RoutedEventArgs e)
    {
        OpenSettingsWindow(0);
    }

    public void OpenSettingsWindow(int initialTab = 0)
    {
        var settingsWin = new SettingsWindow(_settingsManager, _server.PinManager, initialTab, _server)
        {
            Owner = this
        };
        settingsWin.ShowDialog();
    }

    // =========================================================================
    // OS PASSWORD CONFIGURATION MODAL LOGIC
    // =========================================================================

    private void OpenOsPasswordModal(string ip, string displayName, DiscoveredAgent? agent = null)
    {
        _osModalTargetIp = ip;
        _osModalTargetDisplayName = displayName;
        _osModalTargetAgent = agent;
        OsPasswordModalTargetText.Text = $"For {displayName} ({ip})";
        if (_settingsManager.TryGetOsPassword(agent?.MachineId, displayName, ip, out var existingPass))
        {
            OsPasswordModalInput.Password = existingPass;
            OsPasswordModalForgetBtn.Visibility = Visibility.Visible;
        }
        else
        {
            OsPasswordModalInput.Password = string.Empty;
            OsPasswordModalForgetBtn.Visibility = Visibility.Collapsed;
        }
        OsPasswordModalStatusText.Visibility = Visibility.Collapsed;
        OsPasswordModalOverlay.Visibility = Visibility.Visible;
        OsPasswordModalInput.Focus();
        OsPasswordModalInput.SelectAll();
    }

    private void CloseOsPasswordModal_Click(object sender, RoutedEventArgs e)
    {
        OsPasswordModalOverlay.Visibility = Visibility.Collapsed;
        _osModalTargetIp = null;
        _osModalTargetDisplayName = null;
        _osModalTargetAgent = null;
    }

    private void OsPasswordModalOverlay_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource == OsPasswordModalOverlay)
        {
            CloseOsPasswordModal_Click(this, new RoutedEventArgs());
        }
    }

    private void SaveOsPasswordModal_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_osModalTargetIp))
        {
            string pass = OsPasswordModalInput.Password;
            string? machineId = _osModalTargetAgent?.MachineId;
            if (string.IsNullOrEmpty(pass))
            {
                _settingsManager.RemoveOsPassword(machineId, _osModalTargetDisplayName, _osModalTargetIp);
            }
            else
            {
                _settingsManager.SaveOsPassword(machineId, _osModalTargetDisplayName, _osModalTargetIp, pass);
            }
            OsPasswordModalOverlay.Visibility = Visibility.Collapsed;
            SetStatus("Windows OS password updated", Color.FromRgb(16, 185, 129));
        }
    }

    private void OsPasswordModalForgetBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_osModalTargetIp))
        {
            string? machineId = _osModalTargetAgent?.MachineId;
            _settingsManager.RemoveOsPassword(machineId, _osModalTargetDisplayName, _osModalTargetIp);
            OsPasswordModalOverlay.Visibility = Visibility.Collapsed;
            SetStatus("Removed saved OS password", Color.FromRgb(100, 116, 139));
        }
    }

    private void OsPasswordModalInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            SaveOsPasswordModal_Click(sender, e);
        }
        else if (e.Key == Key.Escape)
        {
            CloseOsPasswordModal_Click(sender, e);
        }
    }

    // =========================================================================
    // ELEVATION RESTART LOGIC
    // =========================================================================

    private void RestartAsAdminBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = Environment.ProcessPath ?? Environment.GetCommandLineArgs()[0],
                UseShellExecute = true,
                Verb = "runas"
            };
            System.Diagnostics.Process.Start(startInfo);
            _isExplicitExit = true;
            System.Windows.Application.Current.Shutdown();
        }
        catch (Win32Exception)
        {
            // User cancelled elevation prompt
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to restart as Administrator: {ex.Message}", "Elevation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_isExplicitExit)
        {
            if (_settingsManager.MinimizeToTrayOnClose)
            {
                e.Cancel = true;
                Hide();
                if (!_hasShownTrayTip)
                {
                    _hasShownTrayTip = true;
                    _trayIcon?.ShowBalloonTip(2000, "RemoteLAN", "RemoteLAN is running in the background.", WinForms.ToolTipIcon.Info);
                }
                return;
            }
            else
            {
                ExitApplication();
                return;
            }
        }

        base.OnClosing(e);
    }

    public void ExitApplication()
    {
        _isExplicitExit = true;

        if (_trayIcon != null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _trayIcon = null;
        }

        _discoveryTimer.Stop();
        try
        {
            _hostChatWindow?.Close();
        }
        catch { }
        _hostChatWindow = null;
        _server.Dispose();

        System.Windows.Application.Current.Shutdown();
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_trayIcon != null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _trayIcon = null;
        }

        _discoveryTimer.Stop();
        try
        {
            _hostChatWindow?.Close();
        }
        catch { }
        _hostChatWindow = null;
        _server.Dispose();
        CancelPendingApprovalConnection();
        NetworkChange.NetworkAddressChanged -= NetworkChange_NetworkAddressChanged;
        NetworkChange.NetworkAvailabilityChanged -= NetworkChange_NetworkAvailabilityChanged;
        base.OnClosed(e);
    }
}