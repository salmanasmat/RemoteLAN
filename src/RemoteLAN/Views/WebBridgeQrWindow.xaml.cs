using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using RemoteLAN.Network;
using RemoteLAN.Security;
using RemoteLAN.WebBridge;

namespace RemoteLAN.Views;

public partial class WebBridgeQrWindow : Window
{
    private readonly SettingsManager _settingsManager;
    private readonly PinManager _pinManager;
    private readonly AgentServer? _server;
    private readonly Action? _onOpenSettings;
    private bool _isInitializing = true;

    private sealed class AdapterItem
    {
        public string DisplayText { get; init; } = string.Empty;
        public string IpAddress { get; init; } = string.Empty;
        public bool IsWifi { get; init; }

        public override string ToString() => DisplayText;
    }

    public WebBridgeQrWindow(
        SettingsManager settingsManager,
        PinManager pinManager,
        AgentServer? server = null,
        Action? onOpenSettings = null)
    {
        InitializeComponent();

        _settingsManager = settingsManager;
        _pinManager = pinManager;
        _server = server;
        _onOpenSettings = onOpenSettings;

        // Auto-enable WebBridge if it wasn't running
        if (!_settingsManager.WebBridgeEnabled)
        {
            _settingsManager.WebBridgeEnabled = true;
            _server?.StartWebBridge();
        }

        PinTextBlock.Text = _pinManager.CurrentPin ?? "------";

        PopulateAdapters();
        _isInitializing = false;

        UpdateQr();
        CheckFirewallStatus();
    }

    private void PopulateAdapters()
    {
        AdapterComboBox.Items.Clear();
        var items = new List<AdapterItem>();

        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                bool isWifi = ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ||
                              ni.Name.IndexOf("wi-fi", StringComparison.OrdinalIgnoreCase) >= 0 ||
                              ni.Name.IndexOf("wireless", StringComparison.OrdinalIgnoreCase) >= 0 ||
                              ni.Description.IndexOf("wi-fi", StringComparison.OrdinalIgnoreCase) >= 0 ||
                              ni.Description.IndexOf("wireless", StringComparison.OrdinalIgnoreCase) >= 0;

                var ipProps = ni.GetIPProperties();
                foreach (var addr in ipProps.UnicastAddresses)
                {
                    if (addr.Address.AddressFamily == AddressFamily.InterNetwork)
                    {
                        string ip = addr.Address.ToString();
                        if (ip.StartsWith("169.254.") || ip == "127.0.0.1" || ip == "0.0.0.0") continue;

                        items.Add(new AdapterItem
                        {
                            IpAddress = ip,
                            IsWifi = isWifi,
                            DisplayText = isWifi ? $"📶 Wi-Fi: {ip} (Recommended)" : $"🔌 Ethernet: {ip}"
                        });
                    }
                }
            }
        }
        catch { }

        var sorted = items.OrderByDescending(i => i.IsWifi).ToList();
        if (sorted.Count == 0)
        {
            sorted.Add(new AdapterItem
            {
                IpAddress = "127.0.0.1",
                IsWifi = false,
                DisplayText = "Localhost (127.0.0.1)"
            });
        }

        foreach (var item in sorted)
        {
            AdapterComboBox.Items.Add(item);
        }

        AdapterComboBox.SelectedIndex = 0;
    }

    private void AdapterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;
        UpdateQr();
    }

    private void UpdateQr()
    {
        string ip = "127.0.0.1";
        if (AdapterComboBox?.SelectedItem is AdapterItem selected)
        {
            ip = selected.IpAddress;
        }

        int port = _settingsManager.WebBridgePort;
        string url = $"https://{ip}:{port}";
        UrlTextBlock.Text = url;

        try
        {
            QrCodeImage.Source = QrCodeHelper.GenerateQrCode(url, 7);
        }
        catch (Exception ex)
        {
            DiagnosticLogger.Log($"[WebBridgeQrWindow] Error generating QR code: {ex.Message}");
        }
    }

    private void CheckFirewallStatus()
    {
        int port = _settingsManager.WebBridgePort;
        bool ruleExists = FirewallHelper.IsRulePresent(port);
        FirewallWarningBorder.Visibility = ruleExists ? Visibility.Collapsed : Visibility.Visible;
    }

    private void AllowFirewallBtn_Click(object sender, RoutedEventArgs e)
    {
        int port = _settingsManager.WebBridgePort;
        bool added = FirewallHelper.RequestAddFirewallRuleElevated(port);
        if (added)
        {
            CheckFirewallStatus();
            MessageBox.Show($"Windows Firewall rule for port {port} has been added successfully.", "Firewall Configured", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show("Could not add Windows Firewall rule automatically. Please run RemoteLAN as Administrator or allow port 8443 in Windows Defender Firewall.", "Firewall Notice", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void CopyUrlBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(UrlTextBlock.Text);
            CopyUrlBtn.Content = "Copied!";
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            timer.Tick += (s, args) =>
            {
                CopyUrlBtn.Content = "Copy URL";
                timer.Stop();
            };
            timer.Start();
        }
        catch { }
    }

    private void CopyPinBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(PinTextBlock.Text);
            CopyPinBtn.Content = "Copied!";
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            timer.Tick += (s, args) =>
            {
                CopyPinBtn.Content = "Copy PIN";
                timer.Stop();
            };
            timer.Start();
        }
        catch { }
    }

    private void OpenSettingsBtn_Click(object sender, RoutedEventArgs e)
    {
        Close();
        _onOpenSettings?.Invoke();
    }

    private void CloseBtn_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
