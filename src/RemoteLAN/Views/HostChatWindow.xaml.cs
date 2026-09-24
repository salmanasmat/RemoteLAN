using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using RemoteLAN.Chat;
using RemoteLAN.Network;
using RemoteLAN.Protocol.Messages;
using Color = System.Windows.Media.Color;

namespace RemoteLAN.Views;

/// <summary>
/// AnyDesk/TeamViewer-style floating chat widget for the Agent (host) side.
/// Displayed while a remote Controller is connected. Starts as a compact mini-pill
/// and expands on click or when an incoming message arrives.
/// Fully ephemeral: all chat data is cleared and the window closes on session end.
/// </summary>
public partial class HostChatWindow : Window
{
    private readonly AgentServer _server;
    private readonly ChatViewModel _chatViewModel = new();
    private bool _isExpanded;
    private readonly DispatcherTimer _typingTimer;
    private bool _isClosing;
    private const double CollapsedHeight = 60;   // outer Window height when pill
    private const double ExpandedHeight  = 420;  // outer Window height when open

    public HostChatWindow(AgentServer server, string controllerName)
    {
        InitializeComponent();

        _server = server;
        ControllerNameText.Text = controllerName;
        SubtitleText.Text       = "Session active — click to chat";

        // Position bottom-right of the primary screen
        double screenW = SystemParameters.PrimaryScreenWidth;
        double screenH = SystemParameters.PrimaryScreenHeight;
        Left = screenW - Width - 16;
        Top  = screenH - CollapsedHeight - 56; // 56 px clearance for taskbar

        Height = CollapsedHeight;

        // Typing indicator auto-hide timer
        _typingTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _typingTimer.Tick += (_, _) =>
        {
            _typingTimer.Stop();
            TypingIndicatorText.Visibility = Visibility.Collapsed;
        };

        // Subscribe chat events
        _chatViewModel.UnreadChanged += OnUnreadChanged;
        _server.ChatMessageReceived  += OnControllerMessage;
        _server.ControllerTypingStarted += OnControllerTyping;
        _server.ChatSessionEnded     += OnSessionEnded;
        _server.ClientDisconnected   += OnSessionEnded;
    }

    // ─── Session events ──────────────────────────────────────────────────────

    private void OnControllerMessage(ChatMessagePayload payload)
    {
        Dispatcher.BeginInvoke(() =>
        {
            _chatViewModel.AddMessage(payload, isLocal: false);
            AppendBubble(payload.Text, isLocal: false, payload.SenderName);

            // Auto-expand if still collapsed
            if (!_isExpanded)
            {
                Expand();
            }

            ScrollToBottom();
        });
    }

    private void OnControllerTyping()
    {
        Dispatcher.BeginInvoke(() =>
        {
            TypingIndicatorText.Visibility = Visibility.Visible;
            _typingTimer.Stop();
            _typingTimer.Start();
        });
    }

    public void CloseWindow()
    {
        if (_isClosing) return;
        _isClosing = true;

        if (Dispatcher.CheckAccess())
        {
            try
            {
                _chatViewModel.Clear();
                Close();
            }
            catch { }
        }
        else
        {
            Dispatcher.BeginInvoke(CloseWindow);
        }
    }

    private void OnSessionEnded()
    {
        CloseWindow();
    }

    // ─── UI helpers ──────────────────────────────────────────────────────────

    private void Expand()
    {
        _isExpanded = true;
        BodyRow.Height  = new GridLength(300);
        InputRow.Height = new GridLength(80);
        Height = ExpandedHeight;
        ToggleBtn.Content = "▼";
        SubtitleText.Text = "Session active";

        // When expanded, mark messages read and hide badge
        _chatViewModel.MarkRead();
        UnreadBadge.Visibility = Visibility.Collapsed;

        // Reposition upward to keep bottom edge constant
        double screenH = SystemParameters.PrimaryScreenHeight;
        Top = screenH - ExpandedHeight - 56;
    }

    private void Collapse()
    {
        _isExpanded = false;
        BodyRow.Height  = new GridLength(0);
        InputRow.Height = new GridLength(0);
        Height = CollapsedHeight;
        ToggleBtn.Content = "▲";

        // Move back down
        double screenH = SystemParameters.PrimaryScreenHeight;
        Top = screenH - CollapsedHeight - 56;
    }

    private void AppendBubble(string text, bool isLocal, string? senderName = null)
    {
        // Timestamp label
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

        // Optional sender name for received messages
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
            Margin = isLocal ? new Thickness(48, 4, 4, 0) : new Thickness(4, 4, 48, 0),
            HorizontalAlignment = isLocal ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            MaxWidth = 230,
            Child = bubbleStack
        };

        MessageListPanel.Children.Add(timeText);
        MessageListPanel.Children.Add(bubble);
    }

    private void ScrollToBottom()
    {
        MessageScrollViewer.ScrollToBottom();
    }

    private void OnUnreadChanged()
    {
        Dispatcher.BeginInvoke(() =>
        {
            bool hasUnread = _chatViewModel.HasUnread;
            UnreadBadge.Visibility = hasUnread && !_isExpanded ? Visibility.Visible : Visibility.Collapsed;
            if (hasUnread && !_isExpanded)
            {
                int count = _chatViewModel.Messages.Count;
                UnreadBadgeText.Text = count > 99 ? "99+" : count.ToString();
            }
        });
    }

    // ─── Control events ──────────────────────────────────────────────────────

    private void ToggleBtn_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (_isExpanded) Collapse(); else Expand();
    }

    private void CloseBtn_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        CloseWindow();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Expand on single click, drag on hold
        DragMove();
    }

    private async void SendBtn_Click(object sender, RoutedEventArgs e)
    {
        await SendMessageAsync();
    }

    private async void ChatInputBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !Keyboard.IsKeyDown(Key.LeftShift) && !Keyboard.IsKeyDown(Key.RightShift))
        {
            e.Handled = true;
            await SendMessageAsync();
        }
    }

    private async void ChatInputBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        // Throttled typing indicator
        if (!string.IsNullOrEmpty(ChatInputBox.Text))
        {
            await _server.SendTypingIndicatorAsync();
        }
    }

    private async System.Threading.Tasks.Task SendMessageAsync()
    {
        string text = ChatInputBox.Text.Trim();
        if (string.IsNullOrEmpty(text)) return;

        ChatInputBox.Clear();
        TypingIndicatorText.Visibility = Visibility.Collapsed;

        // Add to local view
        var payload = new ChatMessagePayload
        {
            SenderName = Environment.MachineName,
            TimestampUtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Text = text
        };
        _chatViewModel.AddMessage(payload, isLocal: true);
        AppendBubble(text, isLocal: true);
        ScrollToBottom();

        // Send over the wire
        await _server.SendChatMessageAsync(text);
    }

    // ─── Cleanup ─────────────────────────────────────────────────────────────

    protected override void OnClosed(EventArgs e)
    {
        _typingTimer.Stop();
        _server.ChatMessageReceived     -= OnControllerMessage;
        _server.ControllerTypingStarted -= OnControllerTyping;
        _server.ChatSessionEnded        -= OnSessionEnded;
        _server.ClientDisconnected      -= OnSessionEnded;
        _chatViewModel.Clear();
        base.OnClosed(e);
    }
}
