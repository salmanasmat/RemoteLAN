using System.Threading;
using System.Windows;

namespace RemoteLAN;

public partial class App : Application
{
    private const string MutexName = @"Local\RemoteLAN_SingleInstance_Mutex";
    private const string EventName = @"Local\RemoteLAN_ShowMainWindow_Event";

    private Mutex? _instanceMutex;
    private EventWaitHandle? _showEvent;
    private RegisteredWaitHandle? _waitHandleRegistration;
    private MainWindow? _mainWindow;
    private Network.AgentServer? _headlessServer;
    private Security.SettingsManager? _settings;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        Security.DiagnosticLogger.Log($"[App.OnStartup] Args: '{string.Join(" ", e.Args)}', Session: {System.Diagnostics.Process.GetCurrentProcess().SessionId}, User: {Environment.UserName}, IsAdmin: {Security.DesktopManager.IsAdministrator}, IsSystem: {Security.DesktopManager.IsSystem}");

        for (int i = 0; i < e.Args.Length; i++)
        {
            if (e.Args[i] == "--config" && i + 1 < e.Args.Length)
            {
                Security.SettingsManager.OverrideFilePath = e.Args[i + 1];
            }
        }

        // If launched in Session 0 (started at Windows boot by Task Scheduler under SYSTEM before user logon),
        // run the Session 0 supervisor loop to spawn and maintain the background agent in the active console session.
        if (System.Diagnostics.Process.GetCurrentProcess().SessionId == 0)
        {
            Security.DiagnosticLogger.Log("[App.OnStartup] Process is in Session 0. Launching SessionZeroSupervisor...");
            string cfgPath = Security.SettingsManager.OverrideFilePath ?? Security.SettingsManager.GetDefaultFilePath();
            Security.DesktopManager.RunSessionZeroSupervisor(e.Args, cfgPath);
            Shutdown();
            return;
        }

        // If running as standard user, automatically prompt for Administrator elevation
        if (!Security.DesktopManager.IsAdministrator && !Security.DesktopManager.IsSystem)
        {
            try
            {
                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = Environment.ProcessPath ?? Environment.GetCommandLineArgs()[0],
                    UseShellExecute = true,
                    Verb = "runas"
                };
                foreach (var arg in e.Args)
                {
                    startInfo.ArgumentList.Add(arg);
                }
                System.Diagnostics.Process.Start(startInfo);
                Shutdown();
                return;
            }
            catch
            {
                // User rejected UAC prompt; continue as standard user
            }
        }

        if (Security.DesktopManager.IsAdministrator && !Security.DesktopManager.IsSystem)
        {
            string configPath = Security.SettingsManager.OverrideFilePath ?? Security.SettingsManager.GetDefaultFilePath();
            if (Security.DesktopManager.RelaunchAsSystem(e.Args, configPath))
            {
                Shutdown();
                return;
            }
        }

        bool isFirstInstance;
        try
        {
            _instanceMutex = CreateAccessibleMutex(MutexName, out isFirstInstance);
        }
        catch (Exception ex)
        {
            Security.DiagnosticLogger.Log($"[App.OnStartup] Error creating instance mutex: {ex.Message}");
            isFirstInstance = false;
        }

        if (!isFirstInstance)
        {
            // Another instance is already running!
            // Signal the running instance to reveal itself and come to the foreground, then exit.
            Security.DiagnosticLogger.Log("[App.OnStartup] Another instance is already running; signaling show event and exiting.");
            try
            {
                if (EventWaitHandle.TryOpenExisting(EventName, out var existingEvent))
                {
                    existingEvent.Set();
                    existingEvent.Dispose();
                }
            }
            catch (Exception ex)
            {
                Security.DiagnosticLogger.Log($"[App.OnStartup] Error signaling show event: {ex.Message}");
            }

            Shutdown();
            return;
        }

        // Register event listener for incoming activation signals from future launches
        try
        {
            _showEvent = CreateAccessibleEvent(EventName);
            _waitHandleRegistration = ThreadPool.RegisterWaitForSingleObject(
                _showEvent,
                (state, timedOut) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        Security.DiagnosticLogger.Log("[App] Show event received; activating MainWindow.");
                        EnsureMainWindowCreated();
                        _mainWindow?.ShowAndActivate();
                    });
                },
                null,
                -1,
                false);
        }
        catch (Exception ex)
        {
            Security.DiagnosticLogger.Log($"[App.OnStartup] Error setting up show event: {ex.Message}");
        }

        // Check if application should start hidden in the background
        _settings = new Security.SettingsManager();

        bool isServerLaunch = e.Args.Any(arg =>
            arg.Equals("--server", StringComparison.OrdinalIgnoreCase) ||
            arg.Equals("/server", StringComparison.OrdinalIgnoreCase) ||
            arg.Equals("-server", StringComparison.OrdinalIgnoreCase));

        bool isBackgroundLaunch = isServerLaunch || e.Args.Any(arg =>
            arg.Equals("--background", StringComparison.OrdinalIgnoreCase) ||
            arg.Equals("/background", StringComparison.OrdinalIgnoreCase) ||
            arg.Equals("-background", StringComparison.OrdinalIgnoreCase) ||
            arg.Equals("--minimized", StringComparison.OrdinalIgnoreCase) ||
            arg.Equals("--console-session", StringComparison.OrdinalIgnoreCase));

        bool isConsoleSessionLaunch = e.Args.Any(arg =>
            arg.Equals("--console-session", StringComparison.OrdinalIgnoreCase));

        if (isBackgroundLaunch && !isConsoleSessionLaunch && !isServerLaunch && !Security.StartupHelper.IsRunAtStartupEnabled())
        {
            Shutdown();
            return;
        }

        // Auto-heal scheduled task if startup is enabled but scheduled task is missing
        Security.StartupHelper.EnsureStartupSynchronized();

        bool startInBackground = _settings.StartMinimizedToTray || isBackgroundLaunch;

        // If running in headless server mode (boot supervisor/console-session, --server, or lock screen),
        // initialize the AgentServer headlessly without instantiating WPF MainWindow yet.
        bool shouldRunHeadless = isServerLaunch || isConsoleSessionLaunch || (startInBackground && Security.DesktopManager.IsLockScreenActive());

        if (shouldRunHeadless)
        {
            StartHeadlessServer(_settings);
        }
        else
        {
            _mainWindow = new MainWindow(_settings);

            if (!startInBackground)
            {
                _mainWindow.Show();
                _mainWindow.Activate();
            }
        }
    }

    private void StartHeadlessServer(Security.SettingsManager settings)
    {
        try
        {
            string? savedHostPin = settings.GetHostPin();
            bool unattendedEnabled = settings.IsUnattendedAccessEnabled();
            string? unattendedPassword = settings.GetUnattendedPassword();

            _headlessServer = new Network.AgentServer(
                Protocol.Transport.ProtocolConstants.DefaultPort,
                initialPin: savedHostPin,
                unattendedAccessEnabled: unattendedEnabled,
                unattendedPassword: unattendedPassword,
                settingsManager: settings);

            if (string.IsNullOrWhiteSpace(savedHostPin))
            {
                settings.SaveHostPin(_headlessServer.PinManager.CurrentPin);
            }

            int rotationMinutes = settings.PinRotationIntervalMinutes;
            if (rotationMinutes > 0)
            {
                _headlessServer.PinManager.SetRotationInterval(rotationMinutes);
            }

            _headlessServer.IncomingConnectionRequested += HeadlessServer_IncomingConnectionRequested;
            _headlessServer.Start();
            Security.DiagnosticLogger.Log($"[App.StartHeadlessServer] Headless AgentServer started on port {_headlessServer.Port}. Host: '{Environment.MachineName}'");
        }
        catch (Exception ex)
        {
            Security.DiagnosticLogger.LogException("StartHeadlessServer", ex);
        }
    }

    private void HeadlessServer_IncomingConnectionRequested(Network.IncomingConnectionEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            EnsureMainWindowCreated();
            _mainWindow?.HandleIncomingConnectionRequest(e);
        });
    }

    private void EnsureMainWindowCreated()
    {
        if (_mainWindow == null)
        {
            if (_headlessServer != null)
            {
                _headlessServer.IncomingConnectionRequested -= HeadlessServer_IncomingConnectionRequested;
            }
            _mainWindow = new MainWindow(_settings ?? new Security.SettingsManager(), _headlessServer);
        }
    }

    private static Mutex CreateAccessibleMutex(string name, out bool createdNew)
    {
        try
        {
            var sid = new System.Security.Principal.SecurityIdentifier(System.Security.Principal.WellKnownSidType.WorldSid, null);
            var mutexSecurity = new System.Security.AccessControl.MutexSecurity();
            mutexSecurity.AddAccessRule(new System.Security.AccessControl.MutexAccessRule(
                sid,
                System.Security.AccessControl.MutexRights.FullControl,
                System.Security.AccessControl.AccessControlType.Allow));

            return MutexAcl.Create(true, name, out createdNew, mutexSecurity);
        }
        catch
        {
            return new Mutex(true, name, out createdNew);
        }
    }

    private static EventWaitHandle CreateAccessibleEvent(string name)
    {
        try
        {
            var sid = new System.Security.Principal.SecurityIdentifier(System.Security.Principal.WellKnownSidType.WorldSid, null);
            var eventSecurity = new System.Security.AccessControl.EventWaitHandleSecurity();
            eventSecurity.AddAccessRule(new System.Security.AccessControl.EventWaitHandleAccessRule(
                sid,
                System.Security.AccessControl.EventWaitHandleRights.FullControl,
                System.Security.AccessControl.AccessControlType.Allow));

            return EventWaitHandleAcl.Create(false, EventResetMode.AutoReset, name, out _, eventSecurity);
        }
        catch
        {
            return new EventWaitHandle(false, EventResetMode.AutoReset, name);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _headlessServer?.Dispose();
            _waitHandleRegistration?.Unregister(null);
            _showEvent?.Dispose();
            if (_instanceMutex != null)
            {
                try { _instanceMutex.ReleaseMutex(); } catch { }
                _instanceMutex.Dispose();
            }
        }
        catch { }

        base.OnExit(e);
    }
}

