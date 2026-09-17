using System.IO;

namespace RemoteLAN.Protocol.Discovery;

public static class AgentIdentity
{
    private static readonly object _syncLock = new();
    private static string? _cachedMachineId;

    public static string? OverrideFilePath { get; set; }

    public static string DefaultFilePath
    {
        get
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (string.IsNullOrWhiteSpace(appData))
            {
                appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            }
            return Path.Combine(appData, "RemoteLAN", "agent.id");
        }
    }

    public static string FilePath => OverrideFilePath ?? DefaultFilePath;

    public static string GetOrCreateMachineId()
    {
        lock (_syncLock)
        {
            if (!string.IsNullOrEmpty(_cachedMachineId))
            {
                return _cachedMachineId;
            }

            string path = FilePath;

            try
            {
                if (File.Exists(path))
                {
                    string existing = File.ReadAllText(path).Trim();
                    if (Guid.TryParse(existing, out var parsedGuid))
                    {
                        _cachedMachineId = parsedGuid.ToString("D");
                        return _cachedMachineId;
                    }
                }

                // Check fallback to local appdata if not overridden
                if (OverrideFilePath == null)
                {
                    string localFallback = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "RemoteLAN",
                        "agent.id");

                    if (File.Exists(localFallback))
                    {
                        string existing = File.ReadAllText(localFallback).Trim();
                        if (Guid.TryParse(existing, out var parsedGuid))
                        {
                            _cachedMachineId = parsedGuid.ToString("D");
                            return _cachedMachineId;
                        }
                    }
                }
            }
            catch
            {
                // Fall through to generate and persist
            }

            // Generate new stable ID
            string newId = Guid.NewGuid().ToString("D");

            try
            {
                string? dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                File.WriteAllText(path, newId);
            }
            catch
            {
                // If writing fails (e.g. read-only filesystem), still return the generated ID in-memory
            }

            _cachedMachineId = newId;
            return _cachedMachineId;
        }
    }

    public static void ResetCacheForTesting()
    {
        lock (_syncLock)
        {
            _cachedMachineId = null;
        }
    }
}
