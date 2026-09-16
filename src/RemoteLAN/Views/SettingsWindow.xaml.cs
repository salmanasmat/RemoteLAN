using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RemoteLAN.Security;
using Color = System.Windows.Media.Color;

namespace RemoteLAN.Views;

public partial class SettingsWindow : Window
{
    private readonly SettingsManager _settingsManager;
    private readonly PinManager _pinManager;
    private bool _isInitializing = true;
    private bool _isPasswordVisible;

    public SettingsWindow(SettingsManager settingsManager, PinManager pinManager, int initialTab = 0)
    {
        InitializeComponent();

        _settingsManager = settingsManager;
        _pinManager = pinManager;

        LoadSettings();
        _isInitializing = false;

        SelectTab(initialTab);
    }

    private void SelectTab(int tabIndex)
    {
        switch (tabIndex)
        {
            case 1:
                NavGeneralBtn.IsChecked = true;
                break;
            case 2:
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

        // 3. General & System
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

    // =========================================================================
    // NAVIGATION TABS
    // =========================================================================

    private void NavSecurityBtn_Checked(object sender, RoutedEventArgs e)
    {
        if (SecurityPanel == null) return;
        SecurityPanel.Visibility = Visibility.Visible;
        GeneralPanel.Visibility = Visibility.Collapsed;
        AboutPanel.Visibility = Visibility.Collapsed;
    }

    private void NavGeneralBtn_Checked(object sender, RoutedEventArgs e)
    {
        if (GeneralPanel == null) return;
        SecurityPanel.Visibility = Visibility.Collapsed;
        GeneralPanel.Visibility = Visibility.Visible;
        AboutPanel.Visibility = Visibility.Collapsed;
    }

    private void NavAboutBtn_Checked(object sender, RoutedEventArgs e)
    {
        if (AboutPanel == null) return;
        SecurityPanel.Visibility = Visibility.Collapsed;
        GeneralPanel.Visibility = Visibility.Collapsed;
        AboutPanel.Visibility = Visibility.Visible;
    }

    // =========================================================================
    // SECURITY & ACCESS ACTIONS
    // =========================================================================

    private void EnableUnattendedCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_isInitializing) return;

        bool enabled = EnableUnattendedCheckBox.IsChecked == true;
        UnattendedCredentialsArea.IsEnabled = enabled;

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
