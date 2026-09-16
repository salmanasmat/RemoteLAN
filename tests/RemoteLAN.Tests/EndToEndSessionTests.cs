using RemoteLAN.Network;
using RemoteLAN.Protocol.Messages;
using RemoteLAN.Protocol.Transport;

namespace RemoteLAN.Tests;

public class EndToEndSessionTests
{
    private const int TestPort = 9199;

    [Fact]
    public async Task EndToEnd_Authentication_Success_And_Streaming()
    {
        using var agent = new AgentServer(TestPort, jpegQuality: 60, initialPin: "654321");
        agent.Start();

        try
        {
            using var controller = new ControllerClient();
            var frameReceivedTcs = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);

            controller.FrameReceived += bytes =>
            {
                frameReceivedTcs.TrySetResult(bytes);
            };

            // Connect with valid PIN
            await controller.ConnectAsync("127.0.0.1", TestPort, "654321");

            Assert.Equal(ControllerState.Connected, controller.State);
            Assert.True(controller.RemoteScreenWidth > 0);
            Assert.True(controller.RemoteScreenHeight > 0);

            // Wait for at least one live screen frame to arrive
            var receivedFrame = await Task.WhenAny(frameReceivedTcs.Task, Task.Delay(5000));
            Assert.Same(frameReceivedTcs.Task, receivedFrame);

            byte[] frameBytes = await frameReceivedTcs.Task;
            Assert.NotNull(frameBytes);
            Assert.True(frameBytes.Length > 0);

            // Send inputs (Mouse move, button, key)
            await controller.SendMouseMoveAsync(0.5, 0.5);
            await controller.SendMouseButtonAsync(MouseButtonType.Left, MouseButtonAction.Down);
            await controller.SendMouseButtonAsync(MouseButtonType.Left, MouseButtonAction.Up);
            await controller.SendMouseWheelAsync(120);
            await controller.SendKeyboardKeyAsync(0x41, KeyAction.Down, false);
            await controller.SendKeyboardKeyAsync(0x41, KeyAction.Up, false);

            // Disconnect
            controller.Disconnect();
            Assert.Equal(ControllerState.Disconnected, controller.State);

            // Allow agent to reset
            await Task.Delay(200);

            // Reconnect test: verify agent accepts reconnect without restarting
            using var reconnectController = new ControllerClient();
            await reconnectController.ConnectAsync("127.0.0.1", TestPort, "654321");
            Assert.Equal(ControllerState.Connected, reconnectController.State);

            reconnectController.Disconnect();
        }
        finally
        {
            agent.Stop();
        }
    }

    [Fact]
    public async Task EndToEnd_Authentication_Rejects_Invalid_Pin()
    {
        using var agent = new AgentServer(TestPort + 1, initialPin: "778899");
        agent.Start();

        try
        {
            using var controller = new ControllerClient();
            var stateChangedTcs = new TaskCompletionSource<ControllerState>(TaskCreationOptions.RunContinuationsAsynchronously);

            controller.StateChanged += (state, msg) =>
            {
                if (state == ControllerState.Error || state == ControllerState.Disconnected)
                {
                    stateChangedTcs.TrySetResult(state);
                }
            };

            // Connect with invalid PIN
            await controller.ConnectAsync("127.0.0.1", TestPort + 1, "000000");

            var finalState = await Task.WhenAny(stateChangedTcs.Task, Task.Delay(3000));
            Assert.Same(stateChangedTcs.Task, finalState);
            Assert.Equal(ControllerState.Error, controller.State);
            Assert.False(agent.IsClientConnected);
        }
        finally
        {
            agent.Stop();
        }
    }
}
