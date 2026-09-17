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

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        for (int i = 0; i < e.Args.Length; i++)
        {
            if (e.Args[i] == "--config" && i + 1 < e.Args.Length)
            {
                Security.SettingsManager.OverrideFilePath = e.Args[i + 1];
            }
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
            _instanceMutex = new Mutex(true, MutexName, out isFirstInstance);
        }
        catch
        {
            isFirstInstance = false;
        }

        if (!isFirstInstance)
        {
            // Another instance is already running!
            // Signal the running instance to reveal itself and come to the foreground, then exit.
            try
            {
                if (EventWaitHandle.TryOpenExisting(EventName, out var existingEvent))
                {
                    existingEvent.Set();
                    existingEvent.Dispose();
                }
            }
            catch { }

            Shutdown();
            return;
        }

        // Register event listener for incoming activation signals from future launches
        try
        {
            _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, EventName);
            _waitHandleRegistration = ThreadPool.RegisterWaitForSingleObject(
                _showEvent,
                (state, timedOut) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        _mainWindow?.ShowAndActivate();
                    });
                },
                null,
                -1,
                false);
        }
        catch { }

        // Check if application should start hidden in the background
        var settings = new Security.SettingsManager();
        bool startInBackground = settings.StartMinimizedToTray || e.Args.Any(arg =>
            arg.Equals("--background", StringComparison.OrdinalIgnoreCase) ||
            arg.Equals("/background", StringComparison.OrdinalIgnoreCase) ||
            arg.Equals("-background", StringComparison.OrdinalIgnoreCase) ||
            arg.Equals("--minimized", StringComparison.OrdinalIgnoreCase));

        _mainWindow = new MainWindow();

        if (!startInBackground)
        {
            _mainWindow.Show();
            _mainWindow.Activate();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
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

