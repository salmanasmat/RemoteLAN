using RemoteLAN.Network;
using RemoteLAN.Protocol.Messages;

namespace RemoteLAN.Tests;

public class BidirectionalSessionTests
{
    [Fact]
    public async Task Two_Unified_Nodes_Can_Connect_To_Each_Other_Bidirectionally()
    {
        const int nodeAPort = 9211;
        const int nodeADiscovery = 9212;
        const string pinA = "112233";

        const int nodeBPort = 9213;
        const int nodeBDiscovery = 9214;
        const string pinB = "445566";

        var testInjectorA = new TestInputInjector();
        var testInjectorB = new TestInputInjector();

        // Start Node A (acting as host on 9211)
        using var hostA = new AgentServer(nodeAPort, jpegQuality: 60, initialPin: pinA, discoveryPort: nodeADiscovery, inputInjector: testInjectorA);
        hostA.Start();

        // Start Node B (acting as host on 9213)
        using var hostB = new AgentServer(nodeBPort, jpegQuality: 60, initialPin: pinB, discoveryPort: nodeBDiscovery, inputInjector: testInjectorB);
        hostB.Start();

        try
        {
            // Direction 1: Node A connects OUT to Node B (Port 9213, Pin B)
            using var clientFromA = new ControllerClient();
            var frameFromBTcs = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            clientFromA.FrameReceived += bytes => frameFromBTcs.TrySetResult(bytes);

            await clientFromA.ConnectAsync("127.0.0.1", nodeBPort, pinB);
            Assert.Equal(ControllerState.Connected, clientFromA.State);

            var frameB = await Task.WhenAny(frameFromBTcs.Task, Task.Delay(5000));
            Assert.Same(frameFromBTcs.Task, frameB);
            Assert.NotEmpty(await frameFromBTcs.Task);

            // Direction 2: Node B connects OUT to Node A (Port 9211, Pin A) simultaneously!
            using var clientFromB = new ControllerClient();
            var frameFromATcs = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            clientFromB.FrameReceived += bytes => frameFromATcs.TrySetResult(bytes);

            await clientFromB.ConnectAsync("127.0.0.1", nodeAPort, pinA);
            Assert.Equal(ControllerState.Connected, clientFromB.State);

            var frameA = await Task.WhenAny(frameFromATcs.Task, Task.Delay(5000));
            Assert.Same(frameFromATcs.Task, frameA);
            Assert.NotEmpty(await frameFromATcs.Task);

            var moveATcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            testInjectorB.MouseMoveInjected += _ => moveATcs.TrySetResult(true);

            var moveBTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            testInjectorA.MouseMoveInjected += _ => moveBTcs.TrySetResult(true);

            // Send inputs in both directions
            await clientFromA.SendMouseMoveAsync(0.25, 0.25);
            await clientFromB.SendMouseMoveAsync(0.75, 0.75);

            await Task.WhenAll(
                Task.WhenAny(moveATcs.Task, Task.Delay(3000)),
                Task.WhenAny(moveBTcs.Task, Task.Delay(3000))
            );

            // Verify inputs reached the injectors without moving the physical cursor
            Assert.Contains(testInjectorB.MouseMoves, m => Math.Abs(m.X - 0.25) < 0.001 && Math.Abs(m.Y - 0.25) < 0.001);
            Assert.Contains(testInjectorA.MouseMoves, m => Math.Abs(m.X - 0.75) < 0.001 && Math.Abs(m.Y - 0.75) < 0.001);

            // Disconnect both
            clientFromA.Disconnect();
            clientFromB.Disconnect();

            Assert.Equal(ControllerState.Disconnected, clientFromA.State);
            Assert.Equal(ControllerState.Disconnected, clientFromB.State);
        }
        finally
        {
            hostA.Stop();
            hostB.Stop();
        }
    }

    [Fact]
    public async Task Chat_ControllerToAgent_MessageDelivered()
    {
        const int port = 9215;
        const int discoveryPort = 9216;
        const string pin = "778899";

        var injector = new TestInputInjector();
        using var agent = new AgentServer(port, jpegQuality: 60, initialPin: pin, discoveryPort: discoveryPort, inputInjector: injector);
        agent.Start();

        try
        {
            var chatReceivedTcs = new TaskCompletionSource<RemoteLAN.Protocol.Messages.ChatMessagePayload>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            agent.ChatMessageReceived += msg => chatReceivedTcs.TrySetResult(msg);

            using var controller = new ControllerClient();
            await controller.ConnectAsync("127.0.0.1", port, pin);
            Assert.Equal(ControllerState.Connected, controller.State);

            // Wait for first frame so session is fully established
            var frameTcs = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            controller.FrameReceived += b => frameTcs.TrySetResult(b);
            await Task.WhenAny(frameTcs.Task, Task.Delay(5000));

            const string testMsg = "Hello Agent, this is the Controller!";
            await controller.SendChatMessageAsync(testMsg);

            var completed = await Task.WhenAny(chatReceivedTcs.Task, Task.Delay(4000));
            Assert.Same(chatReceivedTcs.Task, completed);

            var received = await chatReceivedTcs.Task;
            Assert.Equal(testMsg, received.Text);
            Assert.Equal(Environment.MachineName, received.SenderName);

            controller.Disconnect();
        }
        finally
        {
            agent.Stop();
        }
    }

    [Fact]
    public async Task Chat_AgentToController_MessageDelivered()
    {
        const int port = 9217;
        const int discoveryPort = 9218;
        const string pin = "001122";

        var injector = new TestInputInjector();
        using var agent = new AgentServer(port, jpegQuality: 60, initialPin: pin, discoveryPort: discoveryPort, inputInjector: injector);
        agent.Start();

        try
        {
            using var controller = new ControllerClient();
            var chatReceivedTcs = new TaskCompletionSource<RemoteLAN.Protocol.Messages.ChatMessagePayload>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            controller.ChatMessageReceived += msg => chatReceivedTcs.TrySetResult(msg);

            await controller.ConnectAsync("127.0.0.1", port, pin);
            Assert.Equal(ControllerState.Connected, controller.State);

            // Wait for first frame so session is fully established
            var frameTcs = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            controller.FrameReceived += b => frameTcs.TrySetResult(b);
            await Task.WhenAny(frameTcs.Task, Task.Delay(5000));

            const string testMsg = "Hello Controller, this is the Agent!";
            await agent.SendChatMessageAsync(testMsg);

            var completed = await Task.WhenAny(chatReceivedTcs.Task, Task.Delay(4000));
            Assert.Same(chatReceivedTcs.Task, completed);

            var received = await chatReceivedTcs.Task;
            Assert.Equal(testMsg, received.Text);
            Assert.Equal(Environment.MachineName, received.SenderName);

            controller.Disconnect();
        }
        finally
        {
            agent.Stop();
        }
    }
}
