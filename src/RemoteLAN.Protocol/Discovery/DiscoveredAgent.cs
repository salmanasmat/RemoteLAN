namespace RemoteLAN.Protocol.Discovery;

public sealed record DiscoveredAgent
{
    public string MachineName { get; init; } = string.Empty;
    public string IpAddress { get; init; } = string.Empty;
    public int Port { get; init; }
    public string Version { get; init; } = string.Empty;

    public string DisplayText => $"{MachineName} ({IpAddress}:{Port})";

    public string HeaderBackgroundBrush
    {
        get
        {
            // Deterministic pastel palette matching AnyDesk cards.png
            string[] palette = new[]
            {
                "#8497A0", // Slate teal (from cards.png left card)
                "#958CDD", // Soft periwinkle (from cards.png right card)
                "#7E97A6", // Steel blue
                "#8A7EBE", // Soft indigo
                "#6E929E", // Muted ocean
                "#8FA08B", // Sage green
                "#A0897B", // Warm clay
                "#9C7E92"  // Dusty mauve
            };
            int hash = Math.Abs((MachineName + IpAddress).GetHashCode());
            return palette[hash % palette.Length];
        }
    }

    public string StatusDotBrush => "#22C55E"; // Active online green

    public override string ToString() => DisplayText;

    public static bool TryParse(string rawMessage, string senderIp, out DiscoveredAgent? agent)
    {
        agent = null;
        if (string.IsNullOrWhiteSpace(rawMessage) || !rawMessage.StartsWith(DiscoveryConstants.DiscoveryResponsePrefix, StringComparison.Ordinal))
        {
            return false;
        }

        // Expected format: REMOTELAN_AGENT_V1|MachineName|TcpPort|Version
        string payload = rawMessage.Substring(DiscoveryConstants.DiscoveryResponsePrefix.Length);
        string[] parts = payload.Split('|');

        if (parts.Length < 2)
        {
            return false;
        }

        string machineName = parts[0].Trim();
        if (!int.TryParse(parts[1].Trim(), out int port) || port <= 0 || port > 65535)
        {
            return false;
        }

        string version = parts.Length >= 3 ? parts[2].Trim() : "unknown";

        agent = new DiscoveredAgent
        {
            MachineName = machineName,
            IpAddress = senderIp,
            Port = port,
            Version = version
        };

        return true;
    }
}
