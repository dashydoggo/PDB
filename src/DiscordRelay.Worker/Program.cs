using System.Diagnostics;
using System.Runtime.InteropServices;
using DashyDen.DiscordRelay;

namespace DashyDen.DiscordRelay.Worker;

internal static class Program
{
    internal const string Version = "1.0.0";

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

            using var mutex = new Mutex(true, ProductPaths.WorkerMutexName, out bool created);
            if (!created)
            {
                return 0;
            }

            return new RelayWorker().RunAsync().GetAwaiter().GetResult();
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
