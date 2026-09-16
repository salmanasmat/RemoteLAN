using RemoteLAN.Discovery;
using RemoteLAN.Protocol.Discovery;

namespace RemoteLAN.Tests;

public class DiscoveryTests
{
    [Fact]
    public void DiscoveredAgent_TryParse_ValidMessage_ReturnsTrue()
    {
        string raw = "REMOTELAN_AGENT_V1|WORKSTATION-01|9191|0.2.0";
        bool result = DiscoveredAgent.TryParse(raw, "192.168.1.100", out var agent);

        Assert.True(result);
        Assert.NotNull(agent);
        Assert.Equal("WORKSTATION-01", agent.MachineName);
        Assert.Equal("192.168.1.100", agent.IpAddress);
        Assert.Equal(9191, agent.Port);
        Assert.Equal("0.2.0", agent.Version);
        Assert.Equal("WORKSTATION-01 (192.168.1.100:9191)", agent.DisplayText);
        Assert.StartsWith("#", agent.HeaderBackgroundBrush);
        Assert.Equal("#22C55E", agent.StatusDotBrush);
    }

    [Theory]
    [InlineData("")]
    [InlineData("UNKNOWN_PREFIX|WORKSTATION-01|9191")]
    [InlineData("REMOTELAN_AGENT_V1|")]
    [InlineData("REMOTELAN_AGENT_V1|WORKSTATION-01|not-a-port")]
    [InlineData("REMOTELAN_AGENT_V1|WORKSTATION-01|999999")]
    public void DiscoveredAgent_TryParse_InvalidMessage_ReturnsFalse(string invalidRaw)
    {
        bool result = DiscoveredAgent.TryParse(invalidRaw, "127.0.0.1", out var agent);
        Assert.False(result);
        Assert.Null(agent);
    }

    [Fact]
    public async Task EndToEnd_LanDiscovery_Finds_Running_Agent()
    {
        const int testTcpPort = 9193;
        const int testDiscoveryPort = 9194;

        using var responder = new AgentDiscoveryResponder(testTcpPort, testDiscoveryPort);
        responder.Start();

        try
        {
            var discoveryClient = new LanDiscoveryClient(testDiscoveryPort);
            var agents = await discoveryClient.DiscoverAgentsAsync(TimeSpan.FromSeconds(2));

            Assert.NotEmpty(agents);
            var found = agents.FirstOrDefault(a => a.Port == testTcpPort);
            Assert.NotNull(found);
            Assert.Equal(Environment.MachineName, found.MachineName);
            Assert.Equal(testTcpPort, found.Port);
            Assert.Equal(DiscoveryConstants.CurrentProtocolVersion, found.Version);
        }
        finally
        {
            responder.Stop();
        }
    }

    [Fact]
    public async Task EndToEnd_LanDiscovery_WithFilterSelf_Excludes_Local_Machine()
    {
        const int testTcpPort = 9195;
        const int testDiscoveryPort = 9196;

        using var responder = new AgentDiscoveryResponder(testTcpPort, testDiscoveryPort);
        responder.Start();

        try
        {
            var discoveryClient = new LanDiscoveryClient(testDiscoveryPort);
            var agents = await discoveryClient.DiscoverAgentsAsync(TimeSpan.FromSeconds(2), filterSelf: true);

            // Self-filtering must exclude the local machine running on this host
            Assert.DoesNotContain(agents, a => a.Port == testTcpPort || string.Equals(a.MachineName, Environment.MachineName, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            responder.Stop();
        }
    }

    [Fact]
    public async Task IsHostReachableAsync_ReturnsTrue_WhenPortIsOpen()
    {
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;

        try
        {
            bool reachable = await LanDiscoveryClient.IsHostReachableAsync("127.0.0.1", port, timeoutMs: 1500);
            Assert.True(reachable);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task IsHostReachableAsync_ReturnsFalse_WhenPortIsClosed()
    {
        // Bind and immediately close to acquire a definitely-free port
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        int freePort = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        bool reachable = await LanDiscoveryClient.IsHostReachableAsync("127.0.0.1", freePort, timeoutMs: 500);
        Assert.False(reachable);
    }
}
