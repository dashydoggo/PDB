using System.Runtime.InteropServices;

namespace DashyDen.DiscordRelay.Worker;

internal static class NativeMethods
{
    private const uint SoundAsync = 0x0001;
    private const uint SoundNoDefault = 0x0002;
    private const uint SoundFileName = 0x00020000;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    internal static extern int SetCurrentProcessExplicitAppUserModelID(string appUserModelId);

    [DllImport("winmm.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PlaySound(string sound, IntPtr module, uint flags);

    internal static bool PlayCustomWav(string? path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) ||
                !Path.IsPathRooted(path) ||
                !Path.GetExtension(path).Equals(".wav", StringComparison.OrdinalIgnoreCase) ||
                !File.Exists(path) ||
                new FileInfo(path).Length > 50 * 1024 * 1024)
            {
                return false;
            }
            return PlaySound(path, IntPtr.Zero, SoundFileName | SoundAsync | SoundNoDefault);
        }
        catch
        {
            return false;
        }
    }
}
