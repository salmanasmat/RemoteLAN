using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RemoteLAN.Network;
using RemoteLAN.Security;
using RemoteLAN.WebBridge;
using Color = System.Windows.Media.Color;

namespace RemoteLAN.Views;

public partial class SettingsWindow : Window
{
    private readonly SettingsManager _settingsManager;
    private readonly PinManager _pinManager;
    private readonly AgentServer? _server;
    private bool _isInitializing = true;
    private bool _isPasswordVisible;

    public SettingsWindow(SettingsManager settingsManager, PinManager pinManager, int initialTab = 0, AgentServer? server = null)
    {
        InitializeComponent();

        _settingsManager = settingsManager;
        _pinManager = pinManager;
        _server = server;

        LoadSettings();
        _isInitializing = false;

        SelectTab(initialTab);
    }

    private void SelectTab(int tabIndex)
    {
        switch (tabIndex)
        {
            case 1:
                NavWebBridgeBtn.IsChecked = true;
                break;
            case 2:
                NavGeneralBtn.IsChecked = true;
                break;
            case 3:
                NavAboutBtn.IsChecked = true;
                break;
            default:
                NavSecurityBtn.IsChecked = true;
                break;
        }
    }

    private void LoadSettings()
    {
        // 1. Unattended Access
        bool unattended = _pinManager.UnattendedAccessEnabled;
        EnableUnattendedCheckBox.IsChecked = unattended;
        UnattendedCredentialsArea.IsEnabled = unattended;
        UnattendedCredentialsArea.Visibility = unattended ? Visibility.Visible : Visibility.Collapsed;

        string? existingPassword = _pinManager.UnattendedPassword;
        if (!string.IsNullOrEmpty(existingPassword))
        {
            UnattendedPasswordBox.Password = existingPassword;
            UnattendedPasswordTextBox.Text = existingPassword;
            ConfirmPasswordBox.Password = existingPassword;
        }

        // 2. Unauthorized Access & Brute Force Protection
        BlockUnauthorizedCheckBox.IsChecked = _settingsManager.BlockUnauthorizedAttempts;
        BruteForceConfigArea.IsEnabled = _settingsManager.BlockUnauthorizedAttempts;

        int maxAttempts = _settingsManager.MaxFailedAuthAttempts;
        MaxAttemptsComboBox.SelectedIndex = maxAttempts switch
        {
            <= 3 => 0,
            <= 5 => 1,
            _ => 2
        };

        int duration = _settingsManager.LockoutDurationMinutes;
        LockoutDurationComboBox.SelectedIndex = duration switch
        {
            <= 5 => 0,
            <= 10 => 1,
            <= 30 => 2,
            _ => 3
        };

        UpdateLockoutCount();

        // 3. Security PIN Auto-Rotation
        int rotationMinutes = _settingsManager.PinRotationIntervalMinutes;
        PinRotationComboBox.SelectedIndex = rotationMinutes switch
        {
            15 => 1,
            30 => 2,
            60 => 3,
            240 => 4,
            480 => 5,
            1440 => 6,
            _ => 0 // Never
        };

        // 4. WebBridge (Browser Access)
        bool webBridgeEnabled = _settingsManager.WebBridgeEnabled;
        EnableWebBridgeCheckBox.IsChecked = webBridgeEnabled;
        WebBridgeConfigArea.IsEnabled = webBridgeEnabled;
        WebBridgePortTextBox.Text = _settingsManager.WebBridgePort.ToString();
        WebBridgeFpsComboBox.SelectedIndex = _settingsManager.WebBridgeFps switch
        {
            <= 10 => 0,
            <= 15 => 1,
            <= 24 => 2,
            _ => 3
        };
        PopulateWebBridgeAdapters();

        // 5. General & System
        StartWithWindowsCheckBox.IsChecked = StartupHelper.IsRunAtStartupEnabled();
        MinimizeOnCloseCheckBox.IsChecked = _settingsManager.MinimizeToTrayOnClose;
        StartMinimizedCheckBox.IsChecked = _settingsManager.StartMinimizedToTray;
    }

    private void UpdateLockoutCount()
    {
        int count = _settingsManager.GetActiveLockoutsCount();
        LockedOutCountText.Text = count == 1 
            ? "Currently locked out IP addresses: 1 address"
            : $"Currently locked out IP addresses: {count} addresses";
    }

    private sealed class WebBridgeAdapterItem
    {
        public string DisplayText { get; init; } = string.Empty;
        public string IpAddress { get; init; } = string.Empty;
        public bool IsWifi { get; init; }

        public override string ToString() => DisplayText;
    }

    private void PopulateWebBridgeAdapters()
    {
        if (WebBridgeAdapterComboBox == null) return;
        WebBridgeAdapterComboBox.Items.Clear();

        var items = new List<WebBridgeAdapterItem>();
        try
        {
            foreach (var ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback) continue;

                bool isWifi = ni.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Wireless80211 ||
                              ni.Name.IndexOf("wi-fi", StringComparison.OrdinalIgnoreCase) >= 0 ||
                              ni.Name.IndexOf("wireless", StringComparison.OrdinalIgnoreCase) >= 0 ||
                              ni.Description.IndexOf("wi-fi", StringComparison.OrdinalIgnoreCase) >= 0 ||
                              ni.Description.IndexOf("wireless", StringComparison.OrdinalIgnoreCase) >= 0;

                var ipProps = ni.GetIPProperties();
                foreach (var addr in ipProps.UnicastAddresses)
                {
                    if (addr.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    {
                        string ip = addr.Address.ToString();
                        if (ip.StartsWith("169.254.") || ip == "127.0.0.1" || ip == "0.0.0.0") continue;

                        items.Add(new WebBridgeAdapterItem
                        {
                            IpAddress = ip,
                            IsWifi = isWifi,
                            DisplayText = isWifi ? $"📶 Wi-Fi: {ip} (Mobile)" : $"🔌 Ethernet: {ip}"
                        });
                    }
                }
            }
        }
        catch { }

        var sorted = items.OrderByDescending(i => i.IsWifi).ToList();
        if (sorted.Count == 0)
        {
            sorted.Add(new WebBridgeAdapterItem
            {
                IpAddress = "127.0.0.1",
                IsWifi = false,
                DisplayText = "Localhost (127.0.0.1)"
            });
        }

        foreach (var item in sorted)
        {
            WebBridgeAdapterComboBox.Items.Add(item);
        }

        WebBridgeAdapterComboBox.SelectedIndex = 0;
        UpdateWebBridgeUrl();
    }

    private void WebBridgeAdapterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateWebBridgeUrl();
    }

    private void UpdateWebBridgeUrl()
    {
        string localIp = "127.0.0.1";
        if (WebBridgeAdapterComboBox?.SelectedItem is WebBridgeAdapterItem selected)
        {
            localIp = selected.IpAddress;
        }

        int port = _settingsManager.WebBridgePort;
        string url = $"https://{localIp}:{port}";
        if (WebBridgeUrlText != null)
        {
            WebBridgeUrlText.Text = url;
        }

        if (WebBridgeQrImage != null)
        {
            try
            {
                WebBridgeQrImage.Source = QrCodeHelper.GenerateQrCode(url, 6);
            }
            catch (Exception ex)
            {
                DiagnosticLogger.Log($"[SettingsWindow] Error generating QR code: {ex.Message}");
            }
        }

        UpdateWebBridgeFirewallStatus();
    }

    private void UpdateWebBridgeFirewallStatus()
    {
        if (WebBridgeFirewallWarningBorder == null) return;
        int port = _settingsManager.WebBridgePort;
        bool rulePresent = FirewallHelper.IsRulePresent(port);
        WebBridgeFirewallWarningBorder.Visibility = rulePresent ? Visibility.Collapsed : Visibility.Visible;
    }

    private void WebBridgeAllowFirewallBtn_Click(object sender, RoutedEventArgs e)
    {
        int port = _settingsManager.WebBridgePort;
        bool success = FirewallHelper.RequestAddFirewallRuleElevated(port);
        if (success)
        {
            UpdateWebBridgeFirewallStatus();
            MessageBox.Show($"Windows Firewall rule for port {port} has been added successfully.", "Firewall Configured", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show("Could not add Windows Firewall rule automatically. Please run RemoteLAN as Administrator or allow port in Windows Defender Firewall.", "Firewall Notice", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // =========================================================================
    // NAVIGATION TABS
    // =========================================================================

    private void NavSecurityBtn_Checked(object sender, RoutedEventArgs e)
    {
        if (SecurityPanel == null) return;
        SecurityPanel.Visibility = Visibility.Visible;
        if (WebBridgePanel != null) WebBridgePanel.Visibility = Visibility.Collapsed;
        GeneralPanel.Visibility = Visibility.Collapsed;
        AboutPanel.Visibility = Visibility.Collapsed;
    }

    private void NavWebBridgeBtn_Checked(object sender, RoutedEventArgs e)
    {
        if (WebBridgePanel == null) return;
        SecurityPanel.Visibility = Visibility.Collapsed;
        WebBridgePanel.Visibility = Visibility.Visible;
        GeneralPanel.Visibility = Visibility.Collapsed;
        AboutPanel.Visibility = Visibility.Collapsed;
        UpdateWebBridgeUrl();
    }

    private void NavGeneralBtn_Checked(object sender, RoutedEventArgs e)
    {
        if (GeneralPanel == null) return;
        SecurityPanel.Visibility = Visibility.Collapsed;
        if (WebBridgePanel != null) WebBridgePanel.Visibility = Visibility.Collapsed;
        GeneralPanel.Visibility = Visibility.Visible;
        AboutPanel.Visibility = Visibility.Collapsed;
    }

    private void NavAboutBtn_Checked(object sender, RoutedEventArgs e)
    {
        if (AboutPanel == null) return;
        SecurityPanel.Visibility = Visibility.Collapsed;
        if (WebBridgePanel != null) WebBridgePanel.Visibility = Visibility.Collapsed;
        GeneralPanel.Visibility = Visibility.Collapsed;
        AboutPanel.Visibility = Visibility.Visible;
    }

    // =========================================================================
    // WEBBRIDGE (BROWSER ACCESS) ACTIONS
    // =========================================================================

    private void EnableWebBridgeCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_isInitializing) return;
        bool enabled = EnableWebBridgeCheckBox.IsChecked == true;
        _settingsManager.WebBridgeEnabled = enabled;
        WebBridgeConfigArea.IsEnabled = enabled;

        if (_server != null)
        {
            if (enabled)
            {
                _server.StartWebBridge();
            }
            else
            {
                _server.StopWebBridge();
            }
        }
    }

    private void WebBridgePortTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isInitializing) return;
        if (int.TryParse(WebBridgePortTextBox.Text.Trim(), out int port) && port >= 1024 && port <= 65535)
        {
            _settingsManager.WebBridgePort = port;
            UpdateWebBridgeUrl();
        }
    }

    private void WebBridgeFpsComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;
        int fps = WebBridgeFpsComboBox.SelectedIndex switch
        {
            0 => 10,
            1 => 15,
            2 => 24,
            3 => 30,
            _ => 15
        };
        _settingsManager.WebBridgeFps = fps;
    }

    private void CopyWebBridgeUrlBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(WebBridgeUrlText.Text);
            CopyWebBridgeUrlBtn.Content = "Copied!";
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            timer.Tick += (s, args) =>
            {
                CopyWebBridgeUrlBtn.Content = "Copy URL";
                timer.Stop();
            };
            timer.Start();
        }
        catch { }
    }

    // =========================================================================
    // SECURITY & ACCESS ACTIONS
    // =========================================================================

    private void EnableUnattendedCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_isInitializing) return;

        bool enabled = EnableUnattendedCheckBox.IsChecked == true;
        UnattendedCredentialsArea.IsEnabled = enabled;
        UnattendedCredentialsArea.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;

        string currentPassword = _isPasswordVisible 
            ? UnattendedPasswordTextBox.Text 
            : UnattendedPasswordBox.Password;

        _pinManager.ConfigureUnattendedAccess(enabled, string.IsNullOrWhiteSpace(currentPassword) ? null : currentPassword);
        _settingsManager.SetUnattendedAccess(enabled, string.IsNullOrWhiteSpace(currentPassword) ? null : currentPassword);

        if (enabled && string.IsNullOrWhiteSpace(currentPassword))
        {
            UnattendedStatusMessage.Text = "Please set a permanent password.";
            UnattendedStatusMessage.Foreground = new SolidColorBrush(Color.FromRgb(220, 38, 38)); // Red
        }
        else
        {
            UnattendedStatusMessage.Text = enabled ? "Unattended access enabled." : "Unattended access disabled.";
            UnattendedStatusMessage.Foreground = new SolidColorBrush(Color.FromRgb(22, 163, 74)); // Green
        }
    }

    private void TogglePasswordVisibilityBtn_Click(object sender, RoutedEventArgs e)
    {
        _isPasswordVisible = !_isPasswordVisible;

        if (_isPasswordVisible)
        {
            UnattendedPasswordTextBox.Text = UnattendedPasswordBox.Password;
            UnattendedPasswordTextBox.Visibility = Visibility.Visible;
            UnattendedPasswordBox.Visibility = Visibility.Collapsed;
            TogglePasswordVisibilityBtn.Content = "🙈";
        }
        else
        {
            UnattendedPasswordBox.Password = UnattendedPasswordTextBox.Text;
            UnattendedPasswordBox.Visibility = Visibility.Visible;
            UnattendedPasswordTextBox.Visibility = Visibility.Collapsed;
            TogglePasswordVisibilityBtn.Content = "👁️";
        }
    }

    private void SaveUnattendedPasswordBtn_Click(object sender, RoutedEventArgs e)
    {
        string password = _isPasswordVisible 
            ? UnattendedPasswordTextBox.Text.Trim() 
            : UnattendedPasswordBox.Password.Trim();

        string confirm = ConfirmPasswordBox.Password.Trim();

        if (string.IsNullOrEmpty(password))
        {
            UnattendedStatusMessage.Text = "Password cannot be empty.";
            UnattendedStatusMessage.Foreground = new SolidColorBrush(Color.FromRgb(220, 38, 38));
            return;
        }

        if (password.Length < 4)
        {
            UnattendedStatusMessage.Text = "Password must be at least 4 characters long.";
            UnattendedStatusMessage.Foreground = new SolidColorBrush(Color.FromRgb(220, 38, 38));
            return;
        }

        if (password != confirm)
        {
            UnattendedStatusMessage.Text = "Passwords do not match.";
            UnattendedStatusMessage.Foreground = new SolidColorBrush(Color.FromRgb(220, 38, 38));
            return;
        }

        bool isEnabled = EnableUnattendedCheckBox.IsChecked == true;
        _pinManager.ConfigureUnattendedAccess(isEnabled, password);
        _settingsManager.SetUnattendedAccess(isEnabled, password);

        // Keep fields synchronized
        UnattendedPasswordBox.Password = password;
        UnattendedPasswordTextBox.Text = password;
        ConfirmPasswordBox.Password = password;

        UnattendedStatusMessage.Text = "✓ Permanent password saved successfully.";
        UnattendedStatusMessage.Foreground = new SolidColorBrush(Color.FromRgb(22, 163, 74));
    }

    private void BlockUnauthorizedCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_isInitializing) return;

        bool enabled = BlockUnauthorizedCheckBox.IsChecked == true;
        _settingsManager.BlockUnauthorizedAttempts = enabled;
        BruteForceConfigArea.IsEnabled = enabled;
    }

    private void MaxAttemptsComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;

        int attempts = MaxAttemptsComboBox.SelectedIndex switch
        {
            0 => 3,
            1 => 5,
            2 => 10,
            _ => 5
        };

        _settingsManager.MaxFailedAuthAttempts = attempts;
    }

    private void LockoutDurationComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;

        int minutes = LockoutDurationComboBox.SelectedIndex switch
        {
            0 => 5,
            1 => 10,
            2 => 30,
            3 => 60,
            _ => 10
        };

        _settingsManager.LockoutDurationMinutes = minutes;
    }

    private void PinRotationComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;

        int minutes = PinRotationComboBox.SelectedIndex switch
        {
            1 => 15,
            2 => 30,
            3 => 60,
            4 => 240,
            5 => 480,
            6 => 1440,
            _ => 0 // Never
        };

        _settingsManager.PinRotationIntervalMinutes = minutes;
        _pinManager.SetRotationInterval(minutes);
    }

    private void ClearLockoutsBtn_Click(object sender, RoutedEventArgs e)
    {
        _settingsManager.ClearAllLockouts();
        UpdateLockoutCount();
        MessageBox.Show("All locked out IP addresses have been cleared.", "Security Notice", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    // =========================================================================
    // GENERAL & SYSTEM ACTIONS
    // =========================================================================

    private void StartWithWindowsCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_isInitializing) return;

        bool enable = StartWithWindowsCheckBox.IsChecked == true;
        StartupHelper.SetRunAtStartup(enable);
    }

    private void MinimizeOnCloseCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_isInitializing) return;

        _settingsManager.MinimizeToTrayOnClose = MinimizeOnCloseCheckBox.IsChecked == true;
    }

    private void StartMinimizedCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_isInitializing) return;

        _settingsManager.StartMinimizedToTray = StartMinimizedCheckBox.IsChecked == true;
    }

    // =========================================================================
    // ABOUT & DEVELOPER ACTIONS
    // =========================================================================

    private void CopyDeveloperEmailBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText("hello@salmanasmat.com");
            MessageBox.Show("Developer email (hello@salmanasmat.com) copied to clipboard.", "Contact Developer", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch { }
    }

    private void SendDeveloperEmailBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("mailto:hello@salmanasmat.com") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not open email client: {ex.Message}", "Email Error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OpenWebsiteBtn_Click(object sender, RoutedEventArgs e)
    {
        LaunchWebsite();
    }

    private void Hyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        LaunchWebsite();
        e.Handled = true;
    }

    private static void LaunchWebsite()
    {
        try
        {
            Process.Start(new ProcessStartInfo("https://salmanasmat.com") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not open browser: {ex.Message}", "Browser Error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
