using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using DashyDen.DiscordRelay;

namespace DashyDen.DiscordRelay.Worker;

internal static class Program
{
    internal const string Version = "1.0.2";

    [MTAThread]
    private static int Main(string[] args)
    {
        Directory.CreateDirectory(ProductPaths.DataDirectory);
        NativeMethods.SetCurrentProcessExplicitAppUserModelID(ProductPaths.AppUserModelId);

        try
        {
            string settingsExecutable = Path.Combine(AppContext.BaseDirectory, "PDB.exe");
            string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "pdb.png");
            if (File.Exists(settingsExecutable))
            {
                try
                {
                    NotificationIdentityRegistration.Ensure(settingsExecutable, iconPath);
                }
                catch (Exception exception)
                {
                    WorkerLog.Write("Notification identity registration failed: " +
                        WorkerLog.SafeException(exception));
                }
            }

            if (args.Contains("--test-notification", StringComparer.OrdinalIgnoreCase))
            {
                SettingsLoadResult loaded = SettingsStore.Load();
                string testImage = Path.Combine(AppContext.BaseDirectory, "Assets", "pdb.png");
                ToastService.Show(
                    "Discord PDB",
                    loaded.Settings.BodyText,
                    File.Exists(testImage) ? testImage : null,
                    null,
                    loaded.Settings,
                    "test-current");
                WorkerLog.Write("Test notification submitted.");
                return 0;
            }

            if (args.Contains("--diagnose", StringComparer.OrdinalIgnoreCase))
            {
                RelayStatus? status = SettingsStore.LoadStatus();
                string output = status is null
                    ? "No worker status is available."
                    : $"Worker {status.Version}; PID {status.ProcessId}; access {status.AccessStatus}; error {status.Error ?? "none"}.";
                string? outputPath = ValueAfter(args, "--diagnose");
                if (!string.IsNullOrWhiteSpace(outputPath))
                {
                    File.WriteAllText(outputPath, output + Environment.NewLine);
                }
                return 0;
            }

            var heldMutexes = new List<Mutex>();
            try
            {
                var primaryMutex = new Mutex(
                    true,
                    ProductPaths.WorkerMutexNames[0],
                    out bool primaryCreated);
                if (!primaryCreated)
                {
                    primaryMutex.Dispose();
                    return 0;
                }
                heldMutexes.Add(primaryMutex);

                LegacyInstallationCleanup.Run();
                for (int index = 1; index < ProductPaths.WorkerMutexNames.Count; index++)
                {
                    var mutex = new Mutex(true, ProductPaths.WorkerMutexNames[index], out bool created);
                    if (created)
                    {
                        heldMutexes.Add(mutex);
                    }
                    else
                    {
                        mutex.Dispose();
                        WorkerLog.Write("A legacy worker mutex is already held.");
                    }
                }

                return new RelayWorker().RunAsync().GetAwaiter().GetResult();
            }
            finally
            {
                foreach (Mutex mutex in heldMutexes)
                {
                    mutex.Dispose();
                }
            }
        }
        catch (Exception exception)
        {
            WorkerLog.Write("Fatal error: " + WorkerLog.SafeException(exception));
            return 1;
        }
    }

    private static string? ValueAfter(string[] args, string name)
    {
        for (int index = 0; index < args.Length - 1; index++)
        {
            if (args[index].Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return args[index + 1];
            }
        }
        return null;
    }
}

internal static class LegacyInstallationCleanup
{
    internal static void Run()
    {
        try
        {
            using RegistryKey? runKey = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run",
                writable: true);
            runKey?.DeleteValue("Discord Notification Relay", throwOnMissingValue: false);
        }
        catch (Exception exception)
        {
            WorkerLog.Write("Legacy startup cleanup failed: " + WorkerLog.SafeException(exception));
        }

        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        StopOtherPdbProcesses(
            Path.Combine(localAppData, "Programs", "Discord PDB", "PDB.Worker.exe"));
        StopVerifiedLegacyProcess(
            "DiscordRelay.Worker",
            Path.Combine(localAppData, "Programs", "Discord Notification Relay", "DiscordRelay.Worker.exe"));
        StopVerifiedLegacyProcess(
            "DiscordNotificationRelay",
            Path.Combine(localAppData, "DashyDen", "DiscordNotificationRelay", "DiscordNotificationRelay.exe"));
    }

    private static void StopOtherPdbProcesses(string expectedPath)
    {
        foreach (Process process in Process.GetProcessesByName("PDB.Worker"))
        {
            try
            {
                if (process.Id != Environment.ProcessId &&
                    process.MainModule?.FileName.Equals(
                        expectedPath,
                        StringComparison.OrdinalIgnoreCase) == true)
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(5_000);
                    WorkerLog.Write("Stopped an older verified PDB worker process.");
                }
            }
            catch (Exception exception)
            {
                WorkerLog.Write("PDB process cleanup failed: " + WorkerLog.SafeException(exception));
            }
        }
    }

    private static void StopVerifiedLegacyProcess(string processName, string expectedPath)
    {
        foreach (Process process in Process.GetProcessesByName(processName))
        {
            try
            {
                if (process.MainModule?.FileName.Equals(
                        expectedPath,
                        StringComparison.OrdinalIgnoreCase) == true)
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(5_000);
                    WorkerLog.Write("Stopped a verified legacy relay process.");
                }
            }
            catch (Exception exception)
            {
                WorkerLog.Write("Legacy process cleanup failed: " + WorkerLog.SafeException(exception));
            }
        }
    }
}

internal static class WorkerLog
{
    private static readonly object Sync = new();

    internal static void Write(string message)
    {
        try
        {
            lock (Sync)
            {
                Directory.CreateDirectory(ProductPaths.DataDirectory);
                if (File.Exists(ProductPaths.LogFile) &&
                    new FileInfo(ProductPaths.LogFile).Length > 512 * 1024)
                {
                    string previous = ProductPaths.LogFile + ".1";
                    File.Delete(previous);
                    File.Move(ProductPaths.LogFile, previous);
                }
                File.AppendAllText(
                    ProductPaths.LogFile,
                    DateTimeOffset.Now.ToString("O") + " " + message + Environment.NewLine);
            }
        }
        catch
        {
        }
    }

    internal static string SafeException(Exception exception)
    {
        Exception current = exception;
        while (current.InnerException is not null)
        {
            current = current.InnerException;
        }
        return current.GetType().Name + ": " +
            current.Message.Replace('\r', ' ').Replace('\n', ' ');
    }
}
