using System.IO;
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

    [Fact]
    public void DiscoveredAgent_OnlineOfflineStatus_UpdatesProperly()
    {
        var agent = new DiscoveredAgent
        {
            MachineName = "DESKTOP-REMOTE",
            IpAddress = "192.168.1.50",
            Port = 9191,
            Version = "1.0.0",
            IsOnline = true
        };

        var changedProperties = new List<string>();
        agent.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName != null) changedProperties.Add(e.PropertyName);
        };

        Assert.True(agent.IsOnline);
        Assert.Equal("#22C55E", agent.StatusDotBrush);
        Assert.Equal("Online", agent.StatusText);
        Assert.Equal(1.0, agent.CardOpacity);

        // Transition to offline
        agent.IsOnline = false;

        Assert.False(agent.IsOnline);
        Assert.Equal("#94A3B8", agent.StatusDotBrush);
        Assert.StartsWith("Offline", agent.StatusText);
        Assert.Equal(0.65, agent.CardOpacity);

        // Verify property change notifications fired
        Assert.Contains(nameof(DiscoveredAgent.IsOnline), changedProperties);
        Assert.Contains(nameof(DiscoveredAgent.StatusDotBrush), changedProperties);
        Assert.Contains(nameof(DiscoveredAgent.StatusText), changedProperties);
        Assert.Contains(nameof(DiscoveredAgent.CardOpacity), changedProperties);
    }

    [Fact]
    public void AgentIdentity_Generates_Persists_And_Reloads_Guid()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"agent_test_{Guid.NewGuid():N}.id");
        try
        {
            AgentIdentity.OverrideFilePath = tempFile;
            AgentIdentity.ResetCacheForTesting();

            // 1. First run: generates a new GUID and writes to file
            string firstId = AgentIdentity.GetOrCreateMachineId();
            Assert.True(Guid.TryParse(firstId, out _));
            Assert.True(File.Exists(tempFile));
            Assert.Equal(firstId, File.ReadAllText(tempFile).Trim());

            // 2. Reset cache to simulate subsequent app run: should load existing ID from file
            AgentIdentity.ResetCacheForTesting();
            string secondId = AgentIdentity.GetOrCreateMachineId();
            Assert.Equal(firstId, secondId);
        }
        finally
        {
            AgentIdentity.OverrideFilePath = null;
            AgentIdentity.ResetCacheForTesting();
            if (File.Exists(tempFile))
            {
                try { File.Delete(tempFile); } catch { }
            }
        }
    }

    [Fact]
    public void DiscoveredAgent_TryParse_WithMachineIdAndInterface_ReturnsTrue()
    {
        string machineId = Guid.NewGuid().ToString("D");
        string raw = $"REMOTELAN_AGENT_V1|WORKSTATION-01|9191|1.1.6|{machineId}|WiFi|192.168.1.150";
        bool result = DiscoveredAgent.TryParse(raw, "192.168.1.150", out var agent);

        Assert.True(result);
        Assert.NotNull(agent);
        Assert.Equal("WORKSTATION-01", agent.MachineName);
        Assert.Equal(machineId, agent.MachineId);
        Assert.Equal("192.168.1.150", agent.IpAddress);
        Assert.Equal("WiFi", agent.InterfaceType);
        Assert.False(agent.IsEthernet);
        Assert.Single(agent.Endpoints);
        Assert.Equal("192.168.1.150", agent.Endpoints[0].IpAddress);
        Assert.Equal("WiFi", agent.Endpoints[0].InterfaceType);
    }

    [Fact]
    public void DiscoveredAgent_MultiNicEndpoints_Deduplicate_Under_Single_MachineId()
    {
        string machineId = Guid.NewGuid().ToString("D");
        var agent = new DiscoveredAgent
        {
            MachineName = "MULTI-NIC-PC",
            MachineId = machineId,
            Port = 9191,
            Version = "1.1.6"
        };

        // Add first endpoint (WiFi)
        agent.AddOrUpdateEndpoint("192.168.2.50", "WiFi");
        Assert.Single(agent.Endpoints);
        Assert.Equal("192.168.2.50", agent.IpAddress);
        Assert.Equal("WiFi", agent.InterfaceType);

        // Add second endpoint (Ethernet) on same machine
        agent.AddOrUpdateEndpoint("192.168.1.50", "Ethernet");
        Assert.Equal(2, agent.Endpoints.Count);
        Assert.True(agent.HasMultipleEndpoints);

        // Automatically prefers Ethernet over WiFi
        Assert.Equal("192.168.1.50", agent.IpAddress);
        Assert.Equal("Ethernet", agent.InterfaceType);
        Assert.True(agent.IsEthernet);
    }

    [Fact]
    public void DiscoveredAgent_Prefers_Ethernet_Over_WiFi_ByDefault()
    {
        var agent = new DiscoveredAgent
        {
            MachineName = "DESKTOP-FAST",
            MachineId = "TEST-ID-123"
        };

        agent.AddOrUpdateEndpoint("10.0.0.2", "WiFi");
        agent.AddOrUpdateEndpoint("10.0.0.1", "Ethernet");

        Assert.Equal("10.0.0.1", agent.IpAddress);
        Assert.Equal("Ethernet", agent.InterfaceType);
    }

    [Fact]
    public void DiscoveredAgent_Allows_Overriding_Preferred_Endpoint()
    {
        var agent = new DiscoveredAgent
        {
            MachineName = "DESKTOP-TEST",
            MachineId = "TEST-ID-456"
        };

        agent.AddOrUpdateEndpoint("192.168.1.10", "Ethernet");
        agent.AddOrUpdateEndpoint("192.168.2.10", "WiFi");

        // Defaults to Ethernet
        Assert.Equal("192.168.1.10", agent.IpAddress);

        // User manually chooses WiFi endpoint (e.g. from UI dropdown)
        var wifiEndpoint = agent.Endpoints.First(e => e.InterfaceType == "WiFi");
        agent.SelectedEndpoint = wifiEndpoint;

        Assert.Equal("192.168.2.10", agent.IpAddress);
        Assert.Equal("WiFi", agent.InterfaceType);
    }
}
