using System.Collections.ObjectModel;
using System.ComponentModel;
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
    private readonly SettingsManager _settingsManager = new();
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

        // Load persistent host PIN and unattended access credentials
        string? savedHostPin = _settingsManager.GetHostPin();
        bool unattendedEnabled = _settingsManager.IsUnattendedAccessEnabled();
        string? unattendedPassword = _settingsManager.GetUnattendedPassword();

        _server = new AgentServer(
            ProtocolConstants.DefaultPort,
            initialPin: savedHostPin,
            unattendedAccessEnabled: unattendedEnabled,
            unattendedPassword: unattendedPassword);

        // If this is the first run and a PIN was newly generated, persist it
        if (string.IsNullOrWhiteSpace(savedHostPin))
        {
            _settingsManager.SaveHostPin(_server.PinManager.CurrentPin);
        }

        _server.StatusChanged += Server_StatusChanged;
        _server.ClientConnected += Server_ClientConnected;
        _server.ClientDisconnected += Server_ClientDisconnected;
        _server.PinManager.PinChanged += PinManager_PinChanged;
        _server.PinManager.UnattendedAccessChanged += PinManager_UnattendedAccessChanged;

        UpdatePinDisplay(_server.PinManager.CurrentPin);
        UpdateUnattendedUi(unattendedEnabled, unattendedPassword);
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

        // Initialize system tray notification icon
        InitializeTrayIcon();
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
            ShowAndActivate();
            _trayIcon?.ShowBalloonTip(3000, "RemoteLAN Connection", $"Incoming remote control session from {endpoint}", WinForms.ToolTipIcon.Info);
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

    private void CustomPin_Click(object sender, RoutedEventArgs e)
    {
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

    private void PinManager_UnattendedAccessChanged(bool enabled, string? password)
    {
        UpdateUnattendedUi(enabled, password);
    }

    private void UpdateUnattendedUi(bool enabled, string? password)
    {
        Dispatcher.Invoke(() =>
        {
            bool active = enabled && !string.IsNullOrWhiteSpace(password);
            EnableUnattendedCheckBox.IsChecked = active;

            if (active)
            {
                UnattendedStatusBadge.Background = new SolidColorBrush(Color.FromRgb(236, 253, 245)); // Emerald 50
                UnattendedStatusText.Foreground = new SolidColorBrush(Color.FromRgb(5, 150, 105)); // Emerald 600
                UnattendedStatusText.Text = "ACTIVE";
                SetUnattendedPasswordBtn.Content = "Change Password...";
            }
            else
            {
                UnattendedStatusBadge.Background = new SolidColorBrush(Color.FromRgb(241, 245, 249)); // Slate 100
                UnattendedStatusText.Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)); // Slate 500
                UnattendedStatusText.Text = "DISABLED";
                SetUnattendedPasswordBtn.Content = string.IsNullOrWhiteSpace(password) ? "Set Password..." : "Change Password...";
            }
        });
    }

    private void EnableUnattendedCheckBox_Checked(object sender, RoutedEventArgs e)
    {
        string? currentPass = _settingsManager.GetUnattendedPassword();
        if (string.IsNullOrWhiteSpace(currentPass))
        {
            OpenUnattendedModal();
        }
        else
        {
            _settingsManager.SetUnattendedAccess(true);
            _server.PinManager.ConfigureUnattendedAccess(true, currentPass);
            UpdateUnattendedUi(true, currentPass);
        }
    }

    private void EnableUnattendedCheckBox_Unchecked(object sender, RoutedEventArgs e)
    {
        _settingsManager.SetUnattendedAccess(false);
        _server.PinManager.UnattendedAccessEnabled = false;
        UpdateUnattendedUi(false, _settingsManager.GetUnattendedPassword());
    }

    private void SetUnattendedPasswordBtn_Click(object sender, RoutedEventArgs e)
    {
        OpenUnattendedModal();
    }

    private void OpenUnattendedModal()
    {
        UnattendedNewPasswordBox.Password = string.Empty;
        UnattendedConfirmPasswordBox.Password = string.Empty;
        UnattendedModalStatusText.Visibility = Visibility.Collapsed;
        UnattendedModalStatusText.Text = string.Empty;
        UnattendedPasswordModalOverlay.Visibility = Visibility.Visible;
        UnattendedNewPasswordBox.Focus();
    }

    private void CloseUnattendedModal_Click(object sender, RoutedEventArgs e)
    {
        UnattendedPasswordModalOverlay.Visibility = Visibility.Collapsed;
        string? currentPass = _settingsManager.GetUnattendedPassword();
        if (string.IsNullOrWhiteSpace(currentPass))
        {
            EnableUnattendedCheckBox.IsChecked = false;
        }
    }

    private void UnattendedPasswordModalOverlay_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource == UnattendedPasswordModalOverlay)
        {
            CloseUnattendedModal_Click(this, new RoutedEventArgs());
        }
    }

    private void UnattendedPasswordBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            SaveUnattendedPassword_Click(this, new RoutedEventArgs());
        }
        else if (e.Key == Key.Escape)
        {
            CloseUnattendedModal_Click(this, new RoutedEventArgs());
        }
    }

    private void SaveUnattendedPassword_Click(object sender, RoutedEventArgs e)
    {
        string pass1 = UnattendedNewPasswordBox.Password;
        string pass2 = UnattendedConfirmPasswordBox.Password;

        if (string.IsNullOrWhiteSpace(pass1))
        {
            UnattendedModalStatusText.Text = "Please enter a password.";
            UnattendedModalStatusText.Visibility = Visibility.Visible;
            UnattendedNewPasswordBox.Focus();
            return;
        }

        if (pass1.Length < 4)
        {
            UnattendedModalStatusText.Text = "Password must be at least 4 characters long.";
            UnattendedModalStatusText.Visibility = Visibility.Visible;
            UnattendedNewPasswordBox.Focus();
            return;
        }

        if (pass1 != pass2)
        {
            UnattendedModalStatusText.Text = "Passwords do not match. Please re-type.";
            UnattendedModalStatusText.Visibility = Visibility.Visible;
            UnattendedConfirmPasswordBox.Focus();
            return;
        }

        _settingsManager.SetUnattendedAccess(true, pass1);
        _server.PinManager.ConfigureUnattendedAccess(true, pass1);
        UpdateUnattendedUi(true, pass1);
        UnattendedPasswordModalOverlay.Visibility = Visibility.Collapsed;
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
            SetStatus($"Removed saved password for {agent.MachineName}", Color.FromRgb(100, 116, 139));
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
        if (e.Key == Key.Escape)
        {
            if (PinModalOverlay.Visibility == Visibility.Visible)
            {
                ClosePinModal_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
            else if (UnattendedPasswordModalOverlay.Visibility == Visibility.Visible)
            {
                CloseUnattendedModal_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
            else if (CustomCodeModalOverlay.Visibility == Visibility.Visible)
            {
                CloseCustomCodeModal_Click(this, new RoutedEventArgs());
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

    private void InitializeTrayIcon()
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

        var hostItem = new WinForms.ToolStripMenuItem($"Host: {Environment.MachineName}");
        hostItem.Enabled = false;

        var exitItem = new WinForms.ToolStripMenuItem("Exit RemoteLAN");
        exitItem.Click += (s, e) => ExitApplication();

        contextMenu.Items.Add(openItem);
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

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_isExplicitExit)
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
        _server.Dispose();
        base.OnClosed(e);
    }
}