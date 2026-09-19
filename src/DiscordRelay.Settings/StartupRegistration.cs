using System.Diagnostics;
using Microsoft.Win32;

namespace DashyDen.DiscordRelay.SettingsApp;

internal static class StartupRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    internal static void Apply(bool enabled)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        if (!enabled)
        {
            key.DeleteValue(DashyDen.DiscordRelay.ProductPaths.StartupValueName, false);
            return;
        }

        string? workerPath = FindWorkerExecutable();
        if (workerPath is null)
        {
            throw new FileNotFoundException("PDB.Worker.exe was not found beside the settings application.");
        }
        key.SetValue(
            DashyDen.DiscordRelay.ProductPaths.StartupValueName,
            $"\"{workerPath}\" --background",
            RegistryValueKind.String);
    }

    internal static bool EnsureWorkerRunning()
    {
        string? workerPath = FindWorkerExecutable();
        if (workerPath is null)
        {
            return false;
        }

        foreach (Process process in Process.GetProcessesByName("PDB.Worker"))
        {
            try
            {
                if (process.MainModule?.FileName.Equals(
                        workerPath,
                        StringComparison.OrdinalIgnoreCase) == true)
                {
                    return true;
                }
            }
            catch
            {
            }
        }

        Process.Start(new ProcessStartInfo(workerPath, "--background")
        {
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(workerPath)
        });
        return true;
    }

    internal static string? FindWorkerExecutable()
    {
        string besideSettings = Path.Combine(AppContext.BaseDirectory, "PDB.Worker.exe");
        if (File.Exists(besideSettings))
        {
            return besideSettings;
        }

        string installed = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs",
            "Discord PDB",
            "PDB.Worker.exe");
        return File.Exists(installed) ? installed : null;
    }
}
