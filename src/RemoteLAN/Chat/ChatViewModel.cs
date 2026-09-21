using System.Collections.ObjectModel;
using RemoteLAN.Protocol.Messages;

namespace RemoteLAN.Chat;

/// <summary>
/// Represents a single message bubble in the chat UI.
/// </summary>
public sealed record ChatMessageItem(
    string SenderName,
    string Text,
    DateTime Timestamp,
    bool IsLocal);

/// <summary>
/// Ephemeral, in-memory view model for the in-session chat.
/// All data lives exclusively in RAM. Call Clear() on session end to wipe all content.
/// No I/O is ever performed — this type has zero disk footprint by design.
/// </summary>
public sealed class ChatViewModel
{
    private readonly object _lock = new();

    /// <summary>Thread-safe observable collection for UI binding.</summary>
    public ObservableCollection<ChatMessageItem> Messages { get; } = new();

    /// <summary>True when there are unread messages and the drawer is closed.</summary>
    public bool HasUnread { get; private set; }

    /// <summary>Fires when unread state changes so the toolbar badge can refresh.</summary>
    public event Action? UnreadChanged;

    /// <summary>Adds a received or sent message to the in-memory collection.</summary>
    public void AddMessage(ChatMessagePayload payload, bool isLocal)
    {
        var item = new ChatMessageItem(
            SenderName: payload.SenderName,
            Text: payload.Text,
            Timestamp: DateTimeOffset.FromUnixTimeMilliseconds(payload.TimestampUtcMs).LocalDateTime,
            IsLocal: isLocal);

        lock (_lock)
        {
            Messages.Add(item);
            if (!isLocal)
            {
                HasUnread = true;
                UnreadChanged?.Invoke();
            }
        }
    }

    /// <summary>Marks all messages as read (e.g. when chat drawer is opened).</summary>
    public void MarkRead()
    {
        lock (_lock)
        {
            if (!HasUnread) return;
            HasUnread = false;
            UnreadChanged?.Invoke();
        }
    }

    /// <summary>
    /// Destroys all message content and resets state.
    /// Called deterministically on session end — ensures zero data lingers in RAM
    /// beyond the lifetime of the session.
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            Messages.Clear();
            HasUnread = false;
            UnreadChanged = null;
        }
    }
}
