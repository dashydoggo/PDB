namespace DashyDen.DiscordRelay;

public static class ProductPaths
{
    public const string ProductName = "Discord PDB";
    public const string DisplayName = "Discord PDB";
    public const string AppUserModelId = "DashyDen.DiscordPDB";
    public const string StartupValueName = "Discord PDB";
    public const string WorkerMutexName = "Local\\DashyDen.DiscordPDB.Worker";

    public static string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DashyDen",
        "PDB");

    public static string SettingsFile => Path.Combine(DataDirectory, "settings.json");
    public static string SettingsBackupFile => Path.Combine(DataDirectory, "settings.json.bak");
    public static string StatusFile => Path.Combine(DataDirectory, "status.json");
    public static string LogFile => Path.Combine(DataDirectory, "relay.log");

    public static string LegacySettingsFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DashyDen",
        "DiscordNotificationRelay",
        "settings.json");
    public static string AvatarDirectory => Path.Combine(DataDirectory, "avatars");
}
