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
    private Point _lastSentMousePos = new(-1, -1);
    private bool _isFullscreen;

    public SessionWindow(ControllerClient client, string remoteDisplayName, string endpoint)
    {
        InitializeComponent();

        _client = client;
        RemoteHostTitleText.Text = $"Connected to {remoteDisplayName}";
        RemoteEndpointText.Text = endpoint;

        _renderer = new FrameRenderer();
        _renderer.FrameReady += Renderer_FrameReady;
        _renderer.FpsUpdated += Renderer_FpsUpdated;

        _client.FrameReceived += Client_FrameReceived;
        _client.StateChanged += Client_StateChanged;

        ResolutionTextBlock.Text = $"{_client.RemoteScreenWidth}x{_client.RemoteScreenHeight}";
        ViewportContainer.Focus();
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

    private void DisconnectBtn_Click(object sender, RoutedEventArgs e)
    {
        _isUserClosing = true;
        _client.StateChanged -= Client_StateChanged;
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
        base.OnClosed(e);
        _client.Disconnect();
    }
}
