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

    [Fact]
    public async Task EndToEnd_UnattendedAccess_Authenticates_With_Password_And_SessionCode()
    {
        const int port = TestPort + 2;
        const string sessionCode = "7K2M9X";
        const string unattendedPassword = "SuperSecretLanPass2026!";

        using var agent = new AgentServer(port, initialPin: sessionCode, unattendedAccessEnabled: true, unattendedPassword: unattendedPassword);
        agent.Start();

        try
        {
            // 1. Authenticate using unattended password
            using (var controller1 = new ControllerClient())
            {
                await controller1.ConnectAsync("127.0.0.1", port, unattendedPassword);
                Assert.Equal(ControllerState.Connected, controller1.State);
                controller1.Disconnect();
            }

            await Task.Delay(100);

            // 2. Authenticate using alphanumeric session access code (case-insensitive test)
            using (var controller2 = new ControllerClient())
            {
                await controller2.ConnectAsync("127.0.0.1", port, "7k2m9x");
                Assert.Equal(ControllerState.Connected, controller2.State);
                controller2.Disconnect();
            }

            await Task.Delay(100);

            // 3. Disable unattended access and verify unattended password gets rejected
            agent.PinManager.UnattendedAccessEnabled = false;

            using (var controller3 = new ControllerClient())
            {
                var errorTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                controller3.StateChanged += (state, _) =>
                {
                    if (state == ControllerState.Error) errorTcs.TrySetResult(true);
                };

                await controller3.ConnectAsync("127.0.0.1", port, unattendedPassword);
                var res = await Task.WhenAny(errorTcs.Task, Task.Delay(3000));
                Assert.Same(errorTcs.Task, res);
                Assert.Equal(ControllerState.Error, controller3.State);
            }
        }
        finally
        {
            agent.Stop();
        }
    }
}
