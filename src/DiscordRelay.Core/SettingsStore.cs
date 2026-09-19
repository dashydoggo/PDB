using System.Text.Json;
using System.Text.Json.Serialization;

namespace DashyDen.DiscordRelay;

public sealed record SettingsLoadResult(RelaySettings Settings, string? Warning);

public static class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static SettingsLoadResult Load()
    {
        Directory.CreateDirectory(ProductPaths.DataDirectory);
        if (!File.Exists(ProductPaths.SettingsFile) && File.Exists(ProductPaths.LegacySettingsFile))
        {
            try
            {
                File.Copy(ProductPaths.LegacySettingsFile, ProductPaths.SettingsFile, false);
            }
            catch (IOException)
            {
            }
        }
        if (!File.Exists(ProductPaths.SettingsFile))
        {
            var defaults = new RelaySettings().Normalize();
            Save(defaults);
            return new SettingsLoadResult(defaults, null);
        }

        try
        {
            string json = File.ReadAllText(ProductPaths.SettingsFile);
            RelaySettings settings = JsonSerializer.Deserialize<RelaySettings>(json, JsonOptions)
                ?? new RelaySettings();
            settings = settings.Normalize();
            string? warning = settings.Validate().FirstOrDefault();
            return new SettingsLoadResult(settings, warning);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return new SettingsLoadResult(
                new RelaySettings().Normalize(),
                "Settings could not be read; defaults are active. " + exception.Message);
        }
    }

    public static void Save(RelaySettings settings)
    {
        RelaySettings normalized = settings.Normalize();
        IReadOnlyList<string> errors = normalized.Validate();
        if (errors.Count > 0)
        {
            throw new InvalidDataException(errors[0]);
        }

        Directory.CreateDirectory(ProductPaths.DataDirectory);
        string temporaryPath = ProductPaths.SettingsFile + ".tmp";
        string json = JsonSerializer.Serialize(normalized, JsonOptions) + Environment.NewLine;
        File.WriteAllText(temporaryPath, json);

        if (File.Exists(ProductPaths.SettingsFile))
        {
            File.Replace(temporaryPath, ProductPaths.SettingsFile, ProductPaths.SettingsBackupFile, true);
        }
        else
        {
            File.Move(temporaryPath, ProductPaths.SettingsFile);
        }
    }

    public static DateTime GetLastWriteTimeUtc()
    {
        return File.Exists(ProductPaths.SettingsFile)
            ? File.GetLastWriteTimeUtc(ProductPaths.SettingsFile)
            : DateTime.MinValue;
    }

    public static RelayStatus? LoadStatus()
    {
        try
        {
            if (!File.Exists(ProductPaths.StatusFile))
            {
                return null;
            }
            return JsonSerializer.Deserialize<RelayStatus>(
                File.ReadAllText(ProductPaths.StatusFile),
                JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public static void SaveStatus(RelayStatus status)
    {
        Directory.CreateDirectory(ProductPaths.DataDirectory);
        string temporaryPath = ProductPaths.StatusFile + ".tmp";
        File.WriteAllText(
            temporaryPath,
            JsonSerializer.Serialize(status, JsonOptions) + Environment.NewLine);
        if (File.Exists(ProductPaths.StatusFile))
        {
            File.Replace(temporaryPath, ProductPaths.StatusFile, null, true);
        }
        else
        {
            File.Move(temporaryPath, ProductPaths.StatusFile);
        }
    }
}
