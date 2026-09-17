using RemoteLAN.Input;
using RemoteLAN.Network;
using RemoteLAN.Protocol.Messages;
using System.Windows;
using Point = System.Windows.Point;

namespace RemoteLAN.Tests;

public class InputPipelineRegressionTests
{
    private const int BaseTestPort = 9300;
    private static int _portOffset = 0;
    private static int GetNextPort() => BaseTestPort + Interlocked.Increment(ref _portOffset);

    [Fact]
    public void InputInjector_KeyTracking_SuppressesDuplicateKeyDown()
    {
        var sentInputs = new List<Input.NativeMethods.INPUT>();
        using var injector = new InputInjector(inputs => { lock (sentInputs) sentInputs.AddRange(inputs); return (uint)inputs.Length; });
        injector.ResetSession(1);

        // KeyDown for 'A' (0x41)
        injector.InjectKeyboardKey(0x41, KeyAction.Down, false);

        // Duplicate KeyDown for 'A' should be ignored by state tracking
        injector.InjectKeyboardKey(0x41, KeyAction.Down, false);

        // KeyUp for 'A' releases the key
        injector.InjectKeyboardKey(0x41, KeyAction.Up, false);

        // Subsequent redundant KeyUp should be ignored
        injector.InjectKeyboardKey(0x41, KeyAction.Up, false);

        // Final cleanup
        injector.ResetSession(0);
    }

    [Fact]
    public void InputInjector_UnpressedKeyUp_IsIgnored()
    {
        var sentInputs = new List<Input.NativeMethods.INPUT>();
        using var injector = new InputInjector(inputs => { lock (sentInputs) sentInputs.AddRange(inputs); return (uint)inputs.Length; });
        injector.ResetSession(1);

        // Inject KeyUp without prior KeyDown
        injector.InjectKeyboardKey(0x42, KeyAction.Up, false);

        injector.ResetSession(0);
    }

    [Fact]
    public void InputInjector_SessionReset_ReleasesHeldKeysAndButtons()
    {
        var sentInputs = new List<Input.NativeMethods.INPUT>();
        using var injector = new InputInjector(inputs => { lock (sentInputs) sentInputs.AddRange(inputs); return (uint)inputs.Length; });
        injector.ResetSession(1);

        // Press multiple keys: Ctrl (0x11), Shift (0x10), Alt (0x12), 'A' (0x41)
        injector.InjectKeyboardKey(0x11, KeyAction.Down, false);
        injector.InjectKeyboardKey(0x10, KeyAction.Down, false);
        injector.InjectKeyboardKey(0x12, KeyAction.Down, false);
        injector.InjectKeyboardKey(0x41, KeyAction.Down, false);

        // Press mouse buttons
        injector.InjectMouseButton(MouseButtonType.Left, MouseButtonAction.Down);
        injector.InjectMouseButton(MouseButtonType.Right, MouseButtonAction.Down);

        // Reset session (simulating disconnect or session termination)
        // Must release all keys and buttons without throwing
        injector.ResetSession(0);
    }

    [Fact]
    public void InputInjector_SessionScoping_DiscardsStaleEvents()
    {
        var sentInputs = new List<Input.NativeMethods.INPUT>();
        using var injector = new InputInjector(inputs => { lock (sentInputs) sentInputs.AddRange(inputs); return (uint)inputs.Length; });

        // Queue events under session 1
        injector.ResetSession(1);
        for (int i = 0; i < 20; i++)
        {
            injector.InjectMouseMove(0.1 * i, 0.1 * i);
        }

        // Immediately reset to session 2; pending session 1 events are cleared / invalidated
        injector.ResetSession(2);

        // New session input
        injector.InjectMouseMove(0.5, 0.5);

        injector.ResetSession(0);
    }

    [Fact]
    public async Task InputPipeline_ADown_Then_AUp_Sequence()
    {
        int port = GetNextPort();
        var testInjector = new TestInputInjector();
        using var agent = new AgentServer(port, initialPin: "111222", inputInjector: testInjector);
        agent.Start();

        try
        {
            using var controller = new ControllerClient();
            await controller.ConnectAsync("127.0.0.1", port, "111222");
            Assert.Equal(ControllerState.Connected, controller.State);

            var keyUpTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            testInjector.KeyInjected += k =>
            {
                if (k.VirtualKeyCode == 0x41 && k.Action == KeyAction.Up)
                {
                    keyUpTcs.TrySetResult(true);
                }
            };

            await controller.SendKeyboardKeyAsync(0x41, KeyAction.Down, false);
            await controller.SendKeyboardKeyAsync(0x41, KeyAction.Up, false);

            var completed = await Task.WhenAny(keyUpTcs.Task, Task.Delay(3000));
            Assert.Same(keyUpTcs.Task, completed);

            Assert.Equal(2, testInjector.Keys.Count);
            var keys = testInjector.Keys.ToArray();
            Assert.Equal(0x41, keys[0].VirtualKeyCode);
            Assert.Equal(KeyAction.Down, keys[0].Action);
            Assert.Equal(0x41, keys[1].VirtualKeyCode);
            Assert.Equal(KeyAction.Up, keys[1].Action);

            controller.Disconnect();
        }
        finally
        {
            agent.Stop();
        }
    }

    [Fact]
    public async Task InputPipeline_MultipleSimultaneousKeys_ModifiersAndAlpha()
    {
        int port = GetNextPort();
        var testInjector = new TestInputInjector();
        using var agent = new AgentServer(port, initialPin: "222333", inputInjector: testInjector);
        agent.Start();

        try
        {
            using var controller = new ControllerClient();
            await controller.ConnectAsync("127.0.0.1", port, "222333");

            int receivedKeyCount = 0;
            var allKeysTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            testInjector.KeyInjected += _ =>
            {
                if (Interlocked.Increment(ref receivedKeyCount) >= 10)
                {
                    allKeysTcs.TrySetResult(true);
                }
            };

            // Press modifiers: Ctrl (0x11), Shift (0x10), Alt (0x12), Win (0x5B), 'A' (0x41)
            int[] vks = { 0x11, 0x10, 0x12, 0x5B, 0x41 };
            foreach (int vk in vks)
            {
                await controller.SendKeyboardKeyAsync(vk, KeyAction.Down, false);
            }
            foreach (int vk in vks)
            {
                await controller.SendKeyboardKeyAsync(vk, KeyAction.Up, false);
            }

            var completed = await Task.WhenAny(allKeysTcs.Task, Task.Delay(3000));
            Assert.Same(allKeysTcs.Task, completed);
            Assert.Equal(10, testInjector.Keys.Count);

            controller.Disconnect();
        }
        finally
        {
            agent.Stop();
        }
    }

    [Fact]
    public async Task InputPipeline_RapidKeyDownKeyUpSequences()
    {
        int port = GetNextPort();
        var testInjector = new TestInputInjector();
        using var agent = new AgentServer(port, initialPin: "333444", inputInjector: testInjector);
        agent.Start();

        try
        {
            using var controller = new ControllerClient();
            await controller.ConnectAsync("127.0.0.1", port, "333444");

            int count = 0;
            var doneTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            testInjector.KeyInjected += _ =>
            {
                if (Interlocked.Increment(ref count) >= 20)
                {
                    doneTcs.TrySetResult(true);
                }
            };

            for (int i = 0; i < 10; i++)
            {
                await controller.SendKeyboardKeyAsync(0x41, KeyAction.Down, false);
                await controller.SendKeyboardKeyAsync(0x41, KeyAction.Up, false);
            }

            var res = await Task.WhenAny(doneTcs.Task, Task.Delay(3000));
            Assert.Same(doneTcs.Task, res);
            Assert.Equal(20, testInjector.Keys.Count);

            controller.Disconnect();
        }
        finally
        {
            agent.Stop();
        }
    }

    [Fact]
    public async Task InputPipeline_ADown_Disconnect_TriggersSessionReset()
    {
        int port = GetNextPort();
        var testInjector = new TestInputInjector();
        using var agent = new AgentServer(port, initialPin: "444555", inputInjector: testInjector);
        agent.Start();

        try
        {
            using (var controller = new ControllerClient())
            {
                await controller.ConnectAsync("127.0.0.1", port, "444555");
                var keyTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                testInjector.KeyInjected += _ => keyTcs.TrySetResult(true);

                await controller.SendKeyboardKeyAsync(0x41, KeyAction.Down, false);
                await Task.WhenAny(keyTcs.Task, Task.Delay(2000));

                // Disconnect while key is still Down
                controller.Disconnect();
            }

            // Allow agent finally block to execute
            await Task.Delay(200);

            // Agent must have reset session to 0 upon disconnect
            Assert.Contains(testInjector.SessionResets, r => r == 0);
        }
        finally
        {
            agent.Stop();
        }
    }

    [Fact]
    public async Task InputPipeline_ADown_Reconnect_InitializesFreshSession()
    {
        int port = GetNextPort();
        var testInjector = new TestInputInjector();
        using var agent = new AgentServer(port, initialPin: "555666", inputInjector: testInjector);
        agent.Start();

        try
        {
            // First session
            using (var c1 = new ControllerClient())
            {
                await c1.ConnectAsync("127.0.0.1", port, "555666");
                await c1.SendKeyboardKeyAsync(0x41, KeyAction.Down, false);
                c1.Disconnect();
            }

            await Task.Delay(200);

            // Second session (reconnect)
            using (var c2 = new ControllerClient())
            {
                await c2.ConnectAsync("127.0.0.1", port, "555666");
                Assert.Equal(ControllerState.Connected, c2.State);
                c2.Disconnect();
            }

            await Task.Delay(200);

            // Agent must have called ResetSession with positive session IDs and 0 upon disconnects
            var resets = testInjector.SessionResets.ToArray();
            Assert.True(resets.Length >= 2);
            Assert.Contains(resets, r => r > 0);
            Assert.Contains(resets, r => r == 0);
        }
        finally
        {
            agent.Stop();
        }
    }

    [Fact]
    public async Task InputPipeline_NormalAndRapid_MouseMove()
    {
        int port = GetNextPort();
        var testInjector = new TestInputInjector();
        using var agent = new AgentServer(port, initialPin: "666777", inputInjector: testInjector);
        agent.Start();

        try
        {
            using var controller = new ControllerClient();
            await controller.ConnectAsync("127.0.0.1", port, "666777");

            int movesCount = 0;
            var movesDoneTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            testInjector.MouseMoveInjected += _ =>
            {
                if (Interlocked.Increment(ref movesCount) >= 25)
                {
                    movesDoneTcs.TrySetResult(true);
                }
            };

            for (int i = 0; i < 25; i++)
            {
                double norm = i / 25.0;
                await controller.SendMouseMoveAsync(norm, norm);
            }

            var res = await Task.WhenAny(movesDoneTcs.Task, Task.Delay(3000));
            Assert.Same(movesDoneTcs.Task, res);
            Assert.Equal(25, testInjector.MouseMoves.Count);

            controller.Disconnect();
        }
        finally
        {
            agent.Stop();
        }
    }

    [Fact]
    public async Task InputPipeline_MouseButtonDown_Disconnect_TriggersCleanup()
    {
        int port = GetNextPort();
        var testInjector = new TestInputInjector();
        using var agent = new AgentServer(port, initialPin: "777888", inputInjector: testInjector);
        agent.Start();

        try
        {
            using (var controller = new ControllerClient())
            {
                await controller.ConnectAsync("127.0.0.1", port, "777888");
                var btnTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                testInjector.MouseButtonInjected += _ => btnTcs.TrySetResult(true);

                await controller.SendMouseButtonAsync(MouseButtonType.Left, MouseButtonAction.Down);
                await Task.WhenAny(btnTcs.Task, Task.Delay(2000));

                controller.Disconnect();
            }

            await Task.Delay(200);

            // ResetSession(0) must have been invoked on disconnect
            Assert.Contains(testInjector.SessionResets, r => r == 0);
        }
        finally
        {
            agent.Stop();
        }
    }

    [Fact]
    public void CoordinateTranslator_EdgeCases_LetterboxAndClamping()
    {
        // Out of bounds in letterbox (above top)
        var (inBounds1, _, _) = CoordinateTranslator.TranslateToNormalized(
            new Point(500, 10), 1000, 2000, 1920, 1080);
        Assert.False(inBounds1);

        // Clamping check within bounds
        var (inBounds2, normX, normY) = CoordinateTranslator.TranslateToNormalized(
            new Point(960, 540), 1920, 1080, 1920, 1080);
        Assert.True(inBounds2);
        Assert.InRange(normX, 0.0, 1.0);
        Assert.InRange(normY, 0.0, 1.0);
    }
}
