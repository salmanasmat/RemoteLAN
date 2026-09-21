using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using RemoteLAN.Chat;
using RemoteLAN.Input;
using RemoteLAN.Network;
using RemoteLAN.Rendering;
using RemoteLAN.Protocol.Messages;
using Color = System.Windows.Media.Color;
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
    private readonly string? _machineId;
    private readonly string _remoteDisplayName;
    private bool _hasAutoUnlocked;
    private readonly ChatViewModel _chatViewModel = new();
    private readonly DispatcherTimer _typingHideTimer;

    public SessionWindow(ControllerClient client, string remoteDisplayName, string endpoint, Security.SettingsManager? settingsManager = null, string? targetIp = null, string? machineId = null)
    {
        InitializeComponent();

        _client = client;
        _settingsManager = settingsManager;
        _targetIp = targetIp;
        _machineId = machineId;
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
        _client.ChatMessageReceived += OnRemoteChatMessage;
        _client.RemoteTypingStarted += OnRemoteTyping;

        // Typing indicator auto-hide
        _typingHideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _typingHideTimer.Tick += (_, _) =>
        {
            _typingHideTimer.Stop();
            ChatTypingIndicatorText.Visibility = Visibility.Collapsed;
        };

        _chatViewModel.UnreadChanged += UpdateChatBadge;

        ResolutionTextBlock.Text = $"{_client.RemoteScreenWidth}x{_client.RemoteScreenHeight}";
        ViewportContainer.Focus();

        Deactivated += async (s, e) => await ReleaseActiveInputsAsync();
        ViewportContainer.LostFocus += async (s, e) => await ReleaseActiveInputsAsync();

        if (_settingsManager != null && _settingsManager.AutoEnterOsPasswordOnConnect)
        {
            if (_settingsManager.TryGetOsPassword(_machineId, _remoteDisplayName, _targetIp, out var savedPass))
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

    private bool IsRemoteInputEnabled => MenuSendRemoteInputToggle?.IsChecked == true;

    private async void ScreenViewport_MouseMove(object sender, MouseEventArgs e)
    {
        if (OsPasswordPromptOverlay.Visibility != Visibility.Visible && !ViewportContainer.IsFocused)
        {
            ViewportContainer.Focus();
        }

        if (!IsRemoteInputEnabled || _client.State != ControllerState.Connected) return;

        Point pos = e.GetPosition(ScreenViewport);

        // In fullscreen mode, hovering near the top edge peeks the toolbar
        if (_isFullscreen && pos.Y <= 4)
        {
            TopToolbar.Visibility = Visibility.Visible;
        }

        if (_mouseThrottleStopwatch.ElapsedMilliseconds < 16) return;
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
        if (!IsRemoteInputEnabled || _client.State != ControllerState.Connected) return;

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
        if (!IsRemoteInputEnabled || _client.State != ControllerState.Connected) return;

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
        if (!IsRemoteInputEnabled || _client.State != ControllerState.Connected) return;

        await _client.SendMouseWheelAsync(e.Delta);
    }

    private async void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // If OS password prompt modal is open, let the user type locally
        if (OsPasswordPromptOverlay.Visibility == Visibility.Visible) return;

        // Critical: if the chat input box has focus, do NOT forward keys to the remote PC
        if (ChatDrawer.IsKeyboardFocusWithin) return;

        if (!IsRemoteInputEnabled || _client.State != ControllerState.Connected) return;

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

    private async void Window_PreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (OsPasswordPromptOverlay.Visibility == Visibility.Visible) return;
        if (ChatDrawer.IsKeyboardFocusWithin) return;

        if (!IsRemoteInputEnabled || _client.State != ControllerState.Connected) return;

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

    private void TopPeekTrigger_MouseEnter(object sender, MouseEventArgs e)
    {
        if (_isFullscreen)
        {
            TopToolbar.Visibility = Visibility.Visible;
        }
    }

    private void TopToolbar_MouseLeave(object sender, MouseEventArgs e)
    {
        if (_isFullscreen)
        {
            if (ActionsBtn.ContextMenu != null && ActionsBtn.ContextMenu.IsOpen)
            {
                return;
            }
            TopToolbar.Visibility = Visibility.Collapsed;
        }
    }

    private void ToggleFullscreenBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!_isFullscreen)
        {
            EnterFullscreen();
        }
        else
        {
            ExitFullscreen();
        }
    }

    private void EnterFullscreen()
    {
        WindowStyle = WindowStyle.None;
        WindowState = WindowState.Maximized;
        _isFullscreen = true;

        TopToolbarRow.Height = new GridLength(0);
        StatusBarRow.Height = new GridLength(0);
        StatusBarBorder.Visibility = Visibility.Collapsed;

        Grid.SetRowSpan(TopToolbar, 3);
        TopToolbar.VerticalAlignment = VerticalAlignment.Top;
        TopToolbar.Visibility = Visibility.Collapsed;
        TopPeekTrigger.Visibility = Visibility.Visible;

        ToggleFullscreenBtn.Content = "Restore Window";
        ViewportContainer.Focus();
    }

    private void ExitFullscreen()
    {
        Grid.SetRowSpan(TopToolbar, 1);
        TopToolbar.VerticalAlignment = VerticalAlignment.Stretch;
        TopToolbarRow.Height = GridLength.Auto;
        StatusBarRow.Height = GridLength.Auto;

        TopToolbar.Visibility = Visibility.Visible;
        StatusBarBorder.Visibility = Visibility.Visible;
        TopPeekTrigger.Visibility = Visibility.Collapsed;

        WindowStyle = WindowStyle.SingleBorderWindow;
        WindowState = WindowState.Normal;
        _isFullscreen = false;

        ToggleFullscreenBtn.Content = "Fullscreen";
        ViewportContainer.Focus();
    }

    private async void SendCadBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_client.State != ControllerState.Connected) return;

        var menuItem = sender as MenuItem;
        if (menuItem != null) menuItem.IsEnabled = false;

        try
        {
            await _client.SendCtrlAltDelAsync();
            SessionStatusText.Text = "Sent Ctrl+Alt+Del / Wake command to remote PC";
        }
        catch (Exception ex)
        {
            SessionStatusText.Text = $"Failed to send Ctrl+Alt+Del: {ex.Message}";
        }
        finally
        {
            await Task.Delay(500);
            if (menuItem != null) menuItem.IsEnabled = true;
        }
    }

    private void TriggerAutoUnlockAsync(string password)
    {
        if (_hasAutoUnlocked) return;
        _hasAutoUnlocked = true;

        Task.Run(async () =>
        {
            await Task.Delay(1800);

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

        if (_settingsManager != null && _settingsManager.TryGetOsPassword(_machineId, _remoteDisplayName, _targetIp, out var savedPass))
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

        if (_settingsManager != null && _settingsManager.TryGetOsPassword(_machineId, _remoteDisplayName, _targetIp, out var savedPass))
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
        if (_settingsManager != null && _settingsManager.TryGetOsPassword(_machineId, _remoteDisplayName, _targetIp, out var existingPass))
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

        if (_settingsManager != null)
        {
            if (SessionRememberOsPasswordCheckBox.IsChecked == true)
            {
                _settingsManager.SaveOsPassword(_machineId, _remoteDisplayName, _targetIp, password);
            }
            else
            {
                _settingsManager.RemoveOsPassword(_machineId, _remoteDisplayName, _targetIp);
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

    private void ActionsBtn_Click(object sender, RoutedEventArgs e)
    {
        if (ActionsBtn.ContextMenu != null)
        {
            ActionsBtn.ContextMenu.PlacementTarget = ActionsBtn;
            ActionsBtn.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            ActionsBtn.ContextMenu.IsOpen = true;
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

    private async Task HandleSessionEndLockAsync()
    {
        if (MenuLockOnDisconnectToggle.IsChecked && _client.State == ControllerState.Connected)
        {
            try
            {
                await _client.SendPowerActionAsync(PowerActionType.Lock);
                await Task.Delay(150);
            }
            catch { }
        }
    }

    private async void DisconnectBtn_Click(object sender, RoutedEventArgs e)
    {
        _isUserClosing = true;
        _client.StateChanged -= Client_StateChanged;
        await ReleaseActiveInputsAsync();
        await HandleSessionEndLockAsync();
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

    // ─── Chat Handlers ───────────────────────────────────────────────────────

    private void ChatBtn_Click(object sender, RoutedEventArgs e)
    {
        bool nowOpen = ChatDrawer.Visibility != Visibility.Visible;
        ChatDrawer.Visibility = nowOpen ? Visibility.Visible : Visibility.Collapsed;

        if (nowOpen)
        {
            _chatViewModel.MarkRead();
            ChatInputBox.Focus();
            ChatScrollViewer.ScrollToBottom();
        }
        else
        {
            ViewportContainer.Focus();
        }
    }

    private void ChatCloseBtn_Click(object sender, RoutedEventArgs e)
    {
        ChatDrawer.Visibility = Visibility.Collapsed;
        ViewportContainer.Focus();
    }

    private void OnRemoteChatMessage(ChatMessagePayload payload)
    {
        Dispatcher.BeginInvoke(() =>
        {
            _chatViewModel.AddMessage(payload, isLocal: false);
            AppendChatBubble(payload.Text, isLocal: false, senderName: payload.SenderName);

            // Auto-scroll if drawer is open
            if (ChatDrawer.IsVisible)
            {
                _chatViewModel.MarkRead();
                ChatScrollViewer.ScrollToBottom();
            }
        });
    }

    private void OnRemoteTyping()
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (ChatDrawer.IsVisible)
            {
                ChatTypingIndicatorText.Visibility = Visibility.Visible;
                _typingHideTimer.Stop();
                _typingHideTimer.Start();
            }
        });
    }

    private void UpdateChatBadge()
    {
        Dispatcher.BeginInvoke(() =>
        {
            bool show = _chatViewModel.HasUnread && !ChatDrawer.IsVisible;
            ChatUnreadBadge.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            if (show)
            {
                int count = _chatViewModel.Messages.Count;
                ChatUnreadBadgeText.Text = count > 99 ? "99+" : count.ToString();
            }
        });
    }

    private async void ChatSendBtn_Click(object sender, RoutedEventArgs e)
    {
        await SendChatMessageAsync();
    }

    private async void ChatInputBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !Keyboard.IsKeyDown(Key.LeftShift) && !Keyboard.IsKeyDown(Key.RightShift))
        {
            e.Handled = true;
            await SendChatMessageAsync();
        }
    }

    private async void ChatInputBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!string.IsNullOrEmpty(ChatInputBox.Text))
        {
            await _client.SendTypingIndicatorAsync();
        }
    }

    private async Task SendChatMessageAsync()
    {
        string text = ChatInputBox.Text.Trim();
        if (string.IsNullOrEmpty(text)) return;

        ChatInputBox.Clear();
        ChatTypingIndicatorText.Visibility = Visibility.Collapsed;

        // Show locally immediately
        var payload = new ChatMessagePayload
        {
            SenderName = Environment.MachineName,
            TimestampUtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Text = text
        };
        _chatViewModel.AddMessage(payload, isLocal: true);
        AppendChatBubble(text, isLocal: true);
        ChatScrollViewer.ScrollToBottom();

        // Send over the wire
        await _client.SendChatMessageAsync(text);
    }

    private void AppendChatBubble(string text, bool isLocal, string? senderName = null)
    {
        var timeText = new TextBlock
        {
            Text = DateTime.Now.ToString("HH:mm"),
            FontSize = 9,
            Foreground = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8)),
            HorizontalAlignment = isLocal ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            Margin = new Thickness(isLocal ? 0 : 6, 2, isLocal ? 6 : 0, 0)
        };

        var textBlock = new TextBlock
        {
            Text = text,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Foreground = isLocal
                ? new SolidColorBrush(Colors.White)
                : new SolidColorBrush(Color.FromRgb(0x0F, 0x17, 0x2A))
        };

        StackPanel bubbleStack;
        if (!isLocal && !string.IsNullOrEmpty(senderName))
        {
            var nameLabel = new TextBlock
            {
                Text = senderName,
                FontSize = 9,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B)),
                Margin = new Thickness(0, 0, 0, 2)
            };
            bubbleStack = new StackPanel { Children = { nameLabel, textBlock } };
        }
        else
        {
            bubbleStack = new StackPanel { Children = { textBlock } };
        }

        var bubble = new Border
        {
            Background = isLocal
                ? new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB))
                : new SolidColorBrush(Color.FromRgb(0xF1, 0xF5, 0xF9)),
            CornerRadius = isLocal ? new CornerRadius(12, 12, 2, 12) : new CornerRadius(12, 12, 12, 2),
            Padding = new Thickness(10, 6, 10, 6),
            Margin = isLocal ? new Thickness(36, 4, 4, 0) : new Thickness(4, 4, 36, 0),
            HorizontalAlignment = isLocal ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            MaxWidth = 260,
            Child = bubbleStack
        };

        ChatMessageListPanel.Children.Add(timeText);
        ChatMessageListPanel.Children.Add(bubble);
    }

    // ─── Lifecycle ───────────────────────────────────────────────────────────

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_isUserClosing && MenuLockOnDisconnectToggle.IsChecked && _client.State == ControllerState.Connected)
        {
            _isUserClosing = true;
            try
            {
                _client.SendPowerActionAsync(PowerActionType.Lock).AsTask().Wait(250);
            }
            catch { }
        }
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _isUserClosing = true;
        _typingHideTimer.Stop();

        // Unsubscribe chat events first to prevent any late-arriving messages
        // from re-entering Dispatcher after the window is closed.
        _client.ChatMessageReceived -= OnRemoteChatMessage;
        _client.RemoteTypingStarted -= OnRemoteTyping;
        _chatViewModel.Clear();

        _client.StateChanged -= Client_StateChanged;
        _client.FrameReceived -= Client_FrameReceived;
        _ = ReleaseActiveInputsAsync();
        base.OnClosed(e);
        _client.Disconnect();
    }
}
