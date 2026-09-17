using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using RemoteLAN.Input;
using RemoteLAN.Network;
using RemoteLAN.Rendering;
using RemoteLAN.Protocol.Messages;
using Point = System.Windows.Point;

namespace RemoteLAN.Views;

public partial class SessionWindow : Window
{
    private readonly ControllerClient _client;
    private readonly FrameRenderer _renderer;
    private readonly Stopwatch _mouseThrottleStopwatch = Stopwatch.StartNew();
    private readonly HashSet<int> _activePressedKeys = new();
    private readonly HashSet<MouseButtonType> _activePressedButtons = new();
    private Point _lastSentMousePos = new(-1, -1);
    private bool _isFullscreen;
    private readonly Security.SettingsManager? _settingsManager;
    private readonly string? _targetIp;
    private readonly string _remoteDisplayName;
    private bool _hasAutoUnlocked;

    public SessionWindow(ControllerClient client, string remoteDisplayName, string endpoint, Security.SettingsManager? settingsManager = null, string? targetIp = null)
    {
        InitializeComponent();

        _client = client;
        _settingsManager = settingsManager;
        _targetIp = targetIp;
        _remoteDisplayName = remoteDisplayName;

        RemoteHostTitleText.Text = $"Connected to {remoteDisplayName}";
        RemoteEndpointText.Text = endpoint;

        if (_settingsManager != null)
        {
            MenuAutoUnlockToggle.IsChecked = _settingsManager.AutoEnterOsPasswordOnConnect;
        }

        _renderer = new FrameRenderer();
        _renderer.FrameReady += Renderer_FrameReady;
        _renderer.FpsUpdated += Renderer_FpsUpdated;

        _client.FrameReceived += Client_FrameReceived;
        _client.StateChanged += Client_StateChanged;

        ResolutionTextBlock.Text = $"{_client.RemoteScreenWidth}x{_client.RemoteScreenHeight}";
        ViewportContainer.Focus();

        Deactivated += async (s, e) => await ReleaseActiveInputsAsync();
        ViewportContainer.LostFocus += async (s, e) => await ReleaseActiveInputsAsync();

        if (_settingsManager != null && !string.IsNullOrEmpty(_targetIp) && _settingsManager.AutoEnterOsPasswordOnConnect)
        {
            if (_settingsManager.TryGetOsPassword(_targetIp, out var savedPass))
            {
                TriggerAutoUnlockAsync(savedPass);
            }
        }
    }

    private bool _isUserClosing;

    private void Renderer_FrameReady(System.Windows.Media.Imaging.BitmapSource image)
    {
        Dispatcher.BeginInvoke(() =>
        {
            ScreenViewport.Source = image;

            int frameWidth = image.PixelWidth;
            int frameHeight = image.PixelHeight;
            if (frameWidth > 0 && frameHeight > 0 &&
                (frameWidth != _client.RemoteScreenWidth || frameHeight != _client.RemoteScreenHeight))
            {
                _client.UpdateRemoteResolution(frameWidth, frameHeight);
                ResolutionTextBlock.Text = $"{frameWidth}x{frameHeight}";
            }
        });
    }

    private void Renderer_FpsUpdated(double fps)
    {
        Dispatcher.BeginInvoke(() =>
        {
            FpsTextBlock.Text = $"{fps:0.0} FPS";
        });
    }

    private void Client_FrameReceived(byte[] jpegBytes)
    {
        _renderer.ProcessJpegFrame(jpegBytes);
    }

    private void Client_StateChanged(ControllerState state, string message)
    {
        Dispatcher.Invoke(() =>
        {
            if (_isUserClosing) return;

            if (state == ControllerState.Disconnected || state == ControllerState.Error)
            {
                _isUserClosing = true;
                SessionStatusText.Text = $"Session ended: {message}";
                MessageBox.Show($"Remote session disconnected: {message}", "RemoteLAN Session", MessageBoxButton.OK, MessageBoxImage.Information);
                Close();
            }
        });
    }

    private async void ScreenViewport_MouseMove(object sender, MouseEventArgs e)
    {
        if (EnableInputCheckBox.IsChecked != true || _client.State != ControllerState.Connected) return;

        if (_mouseThrottleStopwatch.ElapsedMilliseconds < 16) return;

        Point pos = e.GetPosition(ScreenViewport);
        if (pos == _lastSentMousePos) return;

        var (inBounds, normX, normY) = CoordinateTranslator.TranslateToNormalized(
            pos,
            ScreenViewport.ActualWidth,
            ScreenViewport.ActualHeight,
            _client.RemoteScreenWidth,
            _client.RemoteScreenHeight);

        if (inBounds)
        {
            _mouseThrottleStopwatch.Restart();
            _lastSentMousePos = pos;
            await _client.SendMouseMoveAsync(normX, normY);
        }
    }

    private async void ScreenViewport_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (EnableInputCheckBox.IsChecked != true || _client.State != ControllerState.Connected) return;

        ViewportContainer.Focus();

        MouseButtonType? button = e.ChangedButton switch
        {
            MouseButton.Left => MouseButtonType.Left,
            MouseButton.Right => MouseButtonType.Right,
            MouseButton.Middle => MouseButtonType.Middle,
            _ => null
        };

        if (button.HasValue)
        {
            ScreenViewport.CaptureMouse();
            _activePressedButtons.Add(button.Value);

            Point pos = e.GetPosition(ScreenViewport);
            var (inBounds, normX, normY) = CoordinateTranslator.TranslateToNormalized(
                pos,
                ScreenViewport.ActualWidth,
                ScreenViewport.ActualHeight,
                _client.RemoteScreenWidth,
                _client.RemoteScreenHeight);

            if (inBounds)
            {
                await _client.SendMouseMoveAsync(normX, normY);
                await _client.SendMouseButtonAsync(button.Value, MouseButtonAction.Down);
            }
        }
    }

    private async void ScreenViewport_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (EnableInputCheckBox.IsChecked != true || _client.State != ControllerState.Connected) return;

        ScreenViewport.ReleaseMouseCapture();

        MouseButtonType? button = e.ChangedButton switch
        {
            MouseButton.Left => MouseButtonType.Left,
            MouseButton.Right => MouseButtonType.Right,
            MouseButton.Middle => MouseButtonType.Middle,
            _ => null
        };

        if (button.HasValue)
        {
            _activePressedButtons.Remove(button.Value);
            await _client.SendMouseButtonAsync(button.Value, MouseButtonAction.Up);
        }
    }

    private async void ScreenViewport_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (EnableInputCheckBox.IsChecked != true || _client.State != ControllerState.Connected) return;

        await _client.SendMouseWheelAsync(e.Delta);
    }

    private async void ViewportContainer_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (EnableInputCheckBox.IsChecked != true || _client.State != ControllerState.Connected) return;

        // Ignore auto-repeat to prevent flood of duplicate key downs
        if (e.IsRepeat) return;

        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        int vk = KeyInterop.VirtualKeyFromKey(key);
        if (vk > 0)
        {
            bool isExtended = IsExtendedKey(key);
            _activePressedKeys.Add(vk);
            await _client.SendKeyboardKeyAsync(vk, KeyAction.Down, isExtended);
            e.Handled = true;
        }
    }

    private async void ViewportContainer_PreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (EnableInputCheckBox.IsChecked != true || _client.State != ControllerState.Connected) return;

        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        int vk = KeyInterop.VirtualKeyFromKey(key);
        if (vk > 0)
        {
            bool isExtended = IsExtendedKey(key);
            _activePressedKeys.Remove(vk);
            await _client.SendKeyboardKeyAsync(vk, KeyAction.Up, isExtended);
            e.Handled = true;
        }
    }

    private void ToggleFullscreenBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!_isFullscreen)
        {
            WindowStyle = WindowStyle.None;
            WindowState = WindowState.Maximized;
            _isFullscreen = true;
            ToggleFullscreenBtn.Content = "Restore Window";
        }
        else
        {
            WindowStyle = WindowStyle.SingleBorderWindow;
            WindowState = WindowState.Normal;
            _isFullscreen = false;
            ToggleFullscreenBtn.Content = "Fullscreen";
        }
    }

    private async void SendCadBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_client.State != ControllerState.Connected) return;

        SendCadBtn.IsEnabled = false;
        try
        {
            await _client.SendCtrlAltDelAsync();
            SessionStatusText.Text = "Sent Ctrl+Alt+Del / Wake command to remote PC";
        }
        finally
        {
            await Task.Delay(500);
            SendCadBtn.IsEnabled = true;
        }
    }

    private void TriggerAutoUnlockAsync(string password)
    {
        if (_hasAutoUnlocked) return;
        _hasAutoUnlocked = true;

        Task.Run(async () =>
        {
            await Task.Delay(1500);

            if (_client.State != ControllerState.Connected) return;

            await Dispatcher.InvokeAsync(async () =>
            {
                await DoUnlockAsync(password, isAuto: true);
            });
        });
    }

    private async void UnlockOsBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_client.State != ControllerState.Connected) return;

        if (_settingsManager != null && !string.IsNullOrEmpty(_targetIp) && _settingsManager.TryGetOsPassword(_targetIp, out var savedPass))
        {
            await DoUnlockAsync(savedPass);
        }
        else
        {
            OpenOsPasswordPrompt();
        }
    }

    private async void MenuEnterSavedOsPassword_Click(object sender, RoutedEventArgs e)
    {
        if (_client.State != ControllerState.Connected) return;

        if (_settingsManager != null && !string.IsNullOrEmpty(_targetIp) && _settingsManager.TryGetOsPassword(_targetIp, out var savedPass))
        {
            await DoUnlockAsync(savedPass);
        }
        else
        {
            OpenOsPasswordPrompt();
        }
    }

    private void MenuConfigureOsPassword_Click(object sender, RoutedEventArgs e)
    {
        OpenOsPasswordPrompt();
    }

    private void MenuAutoUnlockToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_settingsManager != null)
        {
            _settingsManager.AutoEnterOsPasswordOnConnect = MenuAutoUnlockToggle.IsChecked;
            _settingsManager.Save();
        }
    }

    private void OpenOsPasswordPrompt()
    {
        if (_settingsManager != null && !string.IsNullOrEmpty(_targetIp) && _settingsManager.TryGetOsPassword(_targetIp, out var existingPass))
        {
            SessionOsPasswordInput.Password = existingPass;
            SessionRememberOsPasswordCheckBox.IsChecked = true;
        }
        else
        {
            SessionOsPasswordInput.Password = string.Empty;
            SessionRememberOsPasswordCheckBox.IsChecked = true;
        }

        OsPasswordPromptOverlay.Visibility = Visibility.Visible;
        SessionOsPasswordInput.Focus();
        SessionOsPasswordInput.SelectAll();
    }

    private void CancelSessionOsPasswordPrompt_Click(object sender, RoutedEventArgs e)
    {
        OsPasswordPromptOverlay.Visibility = Visibility.Collapsed;
        ViewportContainer.Focus();
    }

    private async void SubmitSessionOsPasswordPrompt_Click(object sender, RoutedEventArgs e)
    {
        string password = SessionOsPasswordInput.Password;
        if (string.IsNullOrEmpty(password))
        {
            MessageBox.Show("Please enter the Windows OS password.", "Unlock OS", MessageBoxButton.OK, MessageBoxImage.Information);
            SessionOsPasswordInput.Focus();
            return;
        }

        if (_settingsManager != null && !string.IsNullOrEmpty(_targetIp))
        {
            if (SessionRememberOsPasswordCheckBox.IsChecked == true)
            {
                _settingsManager.SaveOsPassword(_targetIp, password);
            }
            else
            {
                _settingsManager.RemoveOsPassword(_targetIp);
            }
        }

        OsPasswordPromptOverlay.Visibility = Visibility.Collapsed;
        ViewportContainer.Focus();

        await DoUnlockAsync(password);
    }

    private void SessionOsPasswordInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            SubmitSessionOsPasswordPrompt_Click(sender, e);
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            CancelSessionOsPasswordPrompt_Click(sender, e);
        }
    }

    private async Task DoUnlockAsync(string password, bool isAuto = false)
    {
        if (_client.State != ControllerState.Connected) return;

        try
        {
            SessionStatusText.Text = isAuto ? "Auto-entering saved OS password..." : "Sending OS password to remote login screen...";
            await _client.SendUnlockWithOsPasswordAsync(password);
            SessionStatusText.Text = "OS password sent to remote screen. Unlocking...";
        }
        catch (Exception ex)
        {
            SessionStatusText.Text = $"Failed to send OS password: {ex.Message}";
            MessageBox.Show($"Failed to send OS password: {ex.Message}", "Unlock Error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void PowerActionsBtn_Click(object sender, RoutedEventArgs e)
    {
        if (PowerActionsBtn.ContextMenu != null)
        {
            PowerActionsBtn.ContextMenu.PlacementTarget = PowerActionsBtn;
            PowerActionsBtn.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            PowerActionsBtn.ContextMenu.IsOpen = true;
        }
    }

    private async void PowerLock_Click(object sender, RoutedEventArgs e)
    {
        if (_client.State != ControllerState.Connected) return;

        try
        {
            await _client.SendPowerActionAsync(PowerActionType.Lock);
            SessionStatusText.Text = "Lock command sent to remote PC.";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to send lock action: {ex.Message}", "Power Action", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void PowerSleep_Click(object sender, RoutedEventArgs e)
    {
        if (_client.State != ControllerState.Connected) return;

        var result = MessageBox.Show(
            "Are you sure you want to put the remote PC into sleep mode? The remote session will disconnect.",
            "Sleep Remote PC",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes) return;

        try
        {
            await _client.SendPowerActionAsync(PowerActionType.Sleep);
            SessionStatusText.Text = "Sleep command sent to remote PC.";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to send sleep action: {ex.Message}", "Power Action", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void PowerRestart_Click(object sender, RoutedEventArgs e)
    {
        if (_client.State != ControllerState.Connected) return;

        var result = MessageBox.Show(
            "Are you sure you want to restart the remote PC?\n\nAll unsaved work on the remote machine will be lost and the session will disconnect.",
            "Restart Remote PC",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        try
        {
            await _client.SendPowerActionAsync(PowerActionType.Restart);
            SessionStatusText.Text = "Restart command sent to remote PC. Disconnecting...";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to send restart action: {ex.Message}", "Power Action", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void PowerShutdown_Click(object sender, RoutedEventArgs e)
    {
        if (_client.State != ControllerState.Connected) return;

        var result = MessageBox.Show(
            "Are you sure you want to shut down the remote PC?\n\nThe remote machine will turn off and the session will be terminated.",
            "Shutdown Remote PC",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        try
        {
            await _client.SendPowerActionAsync(PowerActionType.Shutdown);
            SessionStatusText.Text = "Shutdown command sent to remote PC. Disconnecting...";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to send shutdown action: {ex.Message}", "Power Action", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async Task ReleaseActiveInputsAsync()
    {
        if (_client.State != ControllerState.Connected) return;

        try
        {
            if (_activePressedButtons.Count > 0)
            {
                var buttons = _activePressedButtons.ToArray();
                _activePressedButtons.Clear();
                foreach (var btn in buttons)
                {
                    await _client.SendMouseButtonAsync(btn, MouseButtonAction.Up);
                }
            }

            if (_activePressedKeys.Count > 0)
            {
                var keys = _activePressedKeys.ToArray();
                _activePressedKeys.Clear();
                foreach (int vk in keys)
                {
                    await _client.SendKeyboardKeyAsync(vk, KeyAction.Up, false);
                }
            }
        }
        catch { }
    }

    private async void DisconnectBtn_Click(object sender, RoutedEventArgs e)
    {
        _isUserClosing = true;
        _client.StateChanged -= Client_StateChanged;
        await ReleaseActiveInputsAsync();
        _client.Disconnect();
        Close();
    }

    private static bool IsExtendedKey(Key key)
    {
        return key switch
        {
            Key.Insert or Key.Delete or Key.Home or Key.End or Key.PageUp or Key.PageDown or
            Key.Left or Key.Right or Key.Up or Key.Down or Key.RightCtrl or Key.RightAlt => true,
            _ => false
        };
    }

    protected override void OnClosed(EventArgs e)
    {
        _isUserClosing = true;
        _client.StateChanged -= Client_StateChanged;
        _client.FrameReceived -= Client_FrameReceived;
        _ = ReleaseActiveInputsAsync();
        base.OnClosed(e);
        _client.Disconnect();
    }
}
