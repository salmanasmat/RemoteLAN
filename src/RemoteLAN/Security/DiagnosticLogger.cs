using System;
using System.Diagnostics;
using System.IO;

namespace RemoteLAN.Security;

/// <summary>
/// Thread-safe and cross-process persistent file logger for early boot, Session 0 supervisor,
/// service lifecycle, and background agent diagnostics.
/// Writes to C:\ProgramData\RemoteLAN\service.log.
/// </summary>
public static class DiagnosticLogger
{
    private static readonly object _fileLock = new();
    private static string? _logFilePath;

    public static string LogFilePath
    {
        get
        {
            if (_logFilePath == null)
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "RemoteLAN");
                try
                {
                    if (!Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }
                }
                catch { }
                _logFilePath = Path.Combine(dir, "service.log");
            }
            return _logFilePath;
        }
        set => _logFilePath = value;
    }

    public static void Log(string message)
    {
        try
        {
            int pid = Environment.ProcessId;
            int sessionId = Process.GetCurrentProcess().SessionId;
            string user = Environment.UserName;
            string line = $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}] [PID:{pid} SESS:{sessionId} USER:{user}] {message}{Environment.NewLine}";

            lock (_fileLock)
            {
                File.AppendAllText(LogFilePath, line);
            }

            Debug.WriteLine(line);
        }
        catch
        {
            // Logging must never crash the host process
        }
    }

    public static void LogException(string context, Exception ex)
    {
        Log($"[EXCEPTION] {context}: {ex.GetType().Name} - {ex.Message}{Environment.NewLine}{ex.StackTrace}");
    }
}
