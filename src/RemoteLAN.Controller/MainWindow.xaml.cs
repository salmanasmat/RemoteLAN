using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using RemoteLAN.Controller.Input;
using RemoteLAN.Controller.Network;
using RemoteLAN.Controller.Rendering;
using RemoteLAN.Protocol.Messages;
using RemoteLAN.Protocol.Transport;

namespace RemoteLAN.Controller;

public partial class MainWindow : Window
{
    private readonly ControllerClient _client;
    private readonly FrameRenderer _renderer;
    private readonly Stopwatch _mouseThrottleStopwatch = Stopwatch.StartNew();
    private Point _lastSentMousePos = new(-1, -1);

    public MainWindow()
    {
        InitializeComponent();

        _renderer = new FrameRenderer();
        _renderer.FrameReady += Renderer_FrameReady;
        _renderer.FpsUpdated += Renderer_FpsUpdated;

        _client = new ControllerClient();
        _client.StateChanged += Client_StateChanged;
        _client.FrameReceived += Client_FrameReceived;
        _client.ScreenResolutionReceived += Client_ScreenResolutionReceived;
    }

    private void Renderer_FrameReady(System.Windows.Media.Imaging.BitmapSource image)
    {
        Dispatcher.Invoke(() =>
        {
            ScreenViewport.Source = image;
        });
    }

    private void Renderer_FpsUpdated(double fps)
    {
        Dispatcher.Invoke(() =>
        {
            FpsTextBlock.Text = $"{fps:0.0} FPS";
        });
    }

    private void Client_ScreenResolutionReceived(int width, int height)
    {
        Dispatcher.Invoke(() =>
        {
            ResolutionTextBlock.Text = $"{width}x{height}";
        });
    }

    private void Client_StateChanged(ControllerState state, string message)
    {
        Dispatcher.Invoke(() =>
        {
            StatusMessageText.Text = message;

            switch (state)
            {
                case ControllerState.Disconnected:
                    StatusDot.Background = new SolidColorBrush(Color.FromRgb(100, 116, 139)); // Slate
                    ConnectBtn.Content = "Connect";
                    ConnectBtn.Background = new SolidColorBrush(Color.FromRgb(37, 99, 235)); // Blue
                    ConnectBtn.IsEnabled = true;
                    PlaceholderPanel.Visibility = Visibility.Visible;
                    ViewportContainer.Visibility = Visibility.Collapsed;
                    ScreenViewport.Source = null;
                    _renderer.Reset();
                    ResolutionTextBlock.Text = "-- x --";
                    break;

                case ControllerState.Connecting:
                    StatusDot.Background = new SolidColorBrush(Color.FromRgb(245, 158, 11)); // Amber
                    ConnectBtn.Content = "Connecting...";
                    ConnectBtn.IsEnabled = false;
                    break;

                case ControllerState.Connected:
                    StatusDot.Background = new SolidColorBrush(Color.FromRgb(16, 185, 129)); // Green
                    ConnectBtn.Content = "Disconnect";
                    ConnectBtn.Background = new SolidColorBrush(Color.FromRgb(239, 68, 68)); // Red
                    ConnectBtn.IsEnabled = true;
                    PlaceholderPanel.Visibility = Visibility.Collapsed;
                    ViewportContainer.Visibility = Visibility.Visible;
                    ViewportContainer.Focus();
                    break;

                case ControllerState.Error:
                    StatusDot.Background = new SolidColorBrush(Color.FromRgb(239, 68, 68)); // Red
                    ConnectBtn.Content = "Connect";
                    ConnectBtn.Background = new SolidColorBrush(Color.FromRgb(37, 99, 235));
                    ConnectBtn.IsEnabled = true;
                    PlaceholderPanel.Visibility = Visibility.Visible;
                    ViewportContainer.Visibility = Visibility.Collapsed;
                    _renderer.Reset();
                    break;
            }
        });
    }

    private void Client_FrameReceived(byte[] jpegBytes)
    {
        _renderer.ProcessJpegFrame(jpegBytes);
    }

    private async void ConnectBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_client.State == ControllerState.Connected || _client.State == ControllerState.Connecting)
        {
            _client.Disconnect();
            return;
        }

        string ip = TargetIpTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(ip))
        {
            MessageBox.Show("Please enter a valid target IP address.", "Input Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!int.TryParse(TargetPortTextBox.Text.Trim(), out int port) || port <= 0 || port > 65535)
        {
            port = ProtocolConstants.DefaultPort;
            TargetPortTextBox.Text = port.ToString();
        }

        string pin = PinTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(pin))
        {
            MessageBox.Show("Please enter the Agent's security PIN.", "Input Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        await _client.ConnectAsync(ip, port, pin);
    }

    private async void ScreenViewport_MouseMove(object sender, MouseEventArgs e)
    {
        if (EnableInputCheckBox.IsChecked != true || _client.State != ControllerState.Connected) return;

        // Throttle mouse moves to ~60Hz
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

        MouseButtonType? button = e.ChangedButton switch
        {
            MouseButton.Left => MouseButtonType.Left,
            MouseButton.Right => MouseButtonType.Right,
            MouseButton.Middle => MouseButtonType.Middle,
            _ => null
        };

        if (button.HasValue)
        {
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

        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        int vk = KeyInterop.VirtualKeyFromKey(key);
        if (vk > 0)
        {
            bool isExtended = IsExtendedKey(key);
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
            await _client.SendKeyboardKeyAsync(vk, KeyAction.Up, isExtended);
            e.Handled = true;
        }
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
        base.OnClosed(e);
        _client.Dispose();
    }
}