using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using Microsoft.Win32;

namespace DashyDen.DiscordRelay;

public static class NotificationIdentityRegistration
{
    private static readonly PropertyKey AppUserModelIdKey = new(
        new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"),
        5);

    public static void Ensure(string settingsExecutable, string iconPath)
    {
        if (!File.Exists(settingsExecutable))
        {
            throw new FileNotFoundException("PDB.exe was not found.", settingsExecutable);
        }

        string shortcutPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
            "Programs",
            "Discord PDB",
            "Discord PDB.lnk");
        Directory.CreateDirectory(Path.GetDirectoryName(shortcutPath)!);

        IShellLinkW shellLink = (IShellLinkW)(object)new ShellLink();
        try
        {
            shellLink.SetPath(settingsExecutable);
            shellLink.SetWorkingDirectory(Path.GetDirectoryName(settingsExecutable)!);
            shellLink.SetDescription("Configure Discord PDB");
            shellLink.SetIconLocation(settingsExecutable, 0);

            IPropertyStore propertyStore = (IPropertyStore)shellLink;
            PropVariant appId = PropVariant.FromString(ProductPaths.AppUserModelId);
            try
            {
                PropertyKey propertyKey = AppUserModelIdKey;
                propertyStore.SetValue(ref propertyKey, ref appId);
                propertyStore.Commit();
            }
            finally
            {
                appId.Dispose();
            }

            ((IPersistFile)shellLink).Save(shortcutPath, true);
        }
        finally
        {
            Marshal.FinalReleaseComObject(shellLink);
        }

        using RegistryKey registryKey = Registry.CurrentUser.CreateSubKey(
            @"Software\Classes\AppUserModelId\" + ProductPaths.AppUserModelId);
        registryKey.SetValue("DisplayName", ProductPaths.DisplayName, RegistryValueKind.String);
        registryKey.SetValue(
            "IconUri",
            File.Exists(iconPath) ? iconPath : settingsExecutable,
            RegistryValueKind.String);
        registryKey.SetValue("IconBackgroundColor", "#8B8D98", RegistryValueKind.String);
    }

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private sealed class ShellLink
    {
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder file,
            int maxPath, IntPtr findData, uint flags);
        void GetIDList(out IntPtr itemIdList);
        void SetIDList(IntPtr itemIdList);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder description,
            int maxName);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string description);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder directory,
            int maxPath);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string directory);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder arguments,
            int maxPath);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);
        void GetHotkey(out short hotkey);
        void SetHotkey(short hotkey);
        void GetShowCmd(out int showCommand);
        void SetShowCmd(int showCommand);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder iconPath,
            int iconPathLength, out int iconIndex);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int iconIndex);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
        void Resolve(IntPtr windowHandle, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    private interface IPropertyStore
    {
        uint GetCount();
        void GetAt(uint propertyIndex, out PropertyKey key);
        void GetValue(ref PropertyKey key, out PropVariant value);
        void SetValue(ref PropertyKey key, ref PropVariant value);
        void Commit();
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct PropertyKey
    {
        internal Guid FormatId;
        internal uint PropertyId;

        internal PropertyKey(Guid formatId, uint propertyId)
        {
            FormatId = formatId;
            PropertyId = propertyId;
        }
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct PropVariant : IDisposable
    {
        [FieldOffset(0)] private ushort _valueType;
        [FieldOffset(8)] private IntPtr _pointerValue;

        internal static PropVariant FromString(string value)
        {
            return new PropVariant
            {
                _valueType = 31,
                _pointerValue = Marshal.StringToCoTaskMemUni(value)
            };
        }

        public void Dispose()
        {
            PropVariantClear(ref this);
        }
    }

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropVariant propVariant);
}
