using RemoteLAN.Network;
using RemoteLAN.Protocol.Messages;
using RemoteLAN.Protocol.Transport;
using RemoteLAN.Security;

namespace RemoteLAN.Tests;

public class EndToEndSessionTests
{
    private const int TestPort = 9199;

    [Fact]
    public async Task EndToEnd_Authentication_Success_And_Streaming()
    {
        var testInjector = new TestInputInjector();
        using var agent = new AgentServer(TestPort, jpegQuality: 60, initialPin: "654321", inputInjector: testInjector);
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

            var keyTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            testInjector.KeyInjected += key =>
            {
                if (key.VirtualKeyCode == 0x41 && key.Action == KeyAction.Up)
                {
                    keyTcs.TrySetResult(true);
                }
            };

            // Send inputs (Mouse move, button, key)
            await controller.SendMouseMoveAsync(0.5, 0.5);
            await controller.SendMouseButtonAsync(MouseButtonType.Left, MouseButtonAction.Down);
            await controller.SendMouseButtonAsync(MouseButtonType.Left, MouseButtonAction.Up);
            await controller.SendMouseWheelAsync(120);
            await controller.SendKeyboardKeyAsync(0x41, KeyAction.Down, false);
            await controller.SendKeyboardKeyAsync(0x41, KeyAction.Up, false);

            var keyCompleted = await Task.WhenAny(keyTcs.Task, Task.Delay(3000));
            Assert.Same(keyTcs.Task, keyCompleted);

            // Verify inputs reached test injector without modifying real host desktop
            Assert.Contains(testInjector.MouseMoves, m => Math.Abs(m.X - 0.5) < 0.001 && Math.Abs(m.Y - 0.5) < 0.001);
            Assert.Contains(testInjector.MouseButtons, b => b.Button == MouseButtonType.Left && b.Action == MouseButtonAction.Down);
            Assert.Contains(testInjector.MouseButtons, b => b.Button == MouseButtonType.Left && b.Action == MouseButtonAction.Up);
            Assert.Contains(testInjector.MouseWheels, w => w == 120);
            Assert.Contains(testInjector.Keys, k => k.VirtualKeyCode == 0x41 && k.Action == KeyAction.Down);
            Assert.Contains(testInjector.Keys, k => k.VirtualKeyCode == 0x41 && k.Action == KeyAction.Up);

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

    [Fact]
    public async Task EndToEnd_Streaming_MaintainsContinuousFrames()
    {
        const int port = 9198;
        using var agent = new AgentServer(port, jpegQuality: 60, initialPin: "123456");
        agent.Start();

        try
        {
            using var controller = new ControllerClient();
            int receivedFrameCount = 0;
            var targetFramesTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            controller.FrameReceived += bytes =>
            {
                if (bytes.Length > 0)
                {
                    int current = Interlocked.Increment(ref receivedFrameCount);
                    if (current >= 5)
                    {
                        targetFramesTcs.TrySetResult(true);
                    }
                }
            };

            await controller.ConnectAsync("127.0.0.1", port, "123456");
            Assert.Equal(ControllerState.Connected, controller.State);

            // Wait up to 5 seconds to receive at least 5 frames continuously
            var completed = await Task.WhenAny(targetFramesTcs.Task, Task.Delay(5000));
            Assert.Same(targetFramesTcs.Task, completed);
            Assert.True(receivedFrameCount >= 5, $"Expected at least 5 frames, got {receivedFrameCount}");

            // Verify controller is still in Connected state (did not disconnect after frame 1)
            Assert.Equal(ControllerState.Connected, controller.State);

            controller.Disconnect();
            Assert.Equal(ControllerState.Disconnected, controller.State);
        }
        finally
        {
            agent.Stop();
        }
    }

    [Fact]
    public async Task EndToEnd_HostAtLockScreen_EmptyPin_ImmediatelyRejectedWithSignInScreenMessage()
    {
        const int port = 9205;
        DesktopManager.MockIsLockScreenActive = true;
        try
        {
            using var agent = new AgentServer(port, initialPin: "888999");
            agent.Start();

            using var controller = new ControllerClient();
            var errorTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

            controller.StateChanged += (state, msg) =>
            {
                if (state == ControllerState.Error)
                {
                    errorTcs.TrySetResult(msg);
                }
            };

            var connectTask = controller.ConnectAsync("127.0.0.1", port, pin: string.Empty);
            var completed = await Task.WhenAny(errorTcs.Task, Task.Delay(3000));

            Assert.Same(errorTcs.Task, completed);
            string errorMsg = await errorTcs.Task;
            Assert.Contains("sign-in screen", errorMsg, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(ControllerState.Error, controller.State);
        }
        finally
        {
            DesktopManager.MockIsLockScreenActive = null;
        }
    }

    [Fact]
    public async Task EndToEnd_HostAtLockScreen_ValidPin_AuthenticatesSuccessfully()
    {
        const int port = 9206;
        DesktopManager.MockIsLockScreenActive = true;
        try
        {
            using var agent = new AgentServer(port, initialPin: "112233");
            agent.Start();

            using var controller = new ControllerClient();
            await controller.ConnectAsync("127.0.0.1", port, pin: "112233");

            Assert.Equal(ControllerState.Connected, controller.State);
            controller.Disconnect();
        }
        finally
        {
            DesktopManager.MockIsLockScreenActive = null;
        }
    }
}
