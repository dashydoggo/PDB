namespace DashyDen.DiscordRelay;

public enum RelayTitleMode
{
    SenderAndChannel,
    DiscordOnly
}

public enum RelaySound
{
    Default,
    InstantMessage,
    Mail,
    Reminder,
    Sms,
    Silent,
    CustomWav
}

public sealed record RelaySettings
{
    public int SchemaVersion { get; init; } = 1;
    public bool Enabled { get; init; } = true;
    public bool StartWithWindows { get; init; } = true;
    public bool IncludeStableDiscord { get; init; } = true;
    public bool IncludeDiscordCanary { get; init; } = true;
    public bool ShowSenderAvatar { get; init; } = true;
    public bool KeepInNotificationCenter { get; init; } = true;
    public bool OpenDiscordOnClick { get; init; } = true;
    public bool OpenDirectMessagesWhenLinkUnavailable { get; init; } = false;
    public RelayTitleMode TitleMode { get; init; } = RelayTitleMode.SenderAndChannel;
    public string BodyText { get; init; } = "New message";
    public RelaySound Sound { get; init; } = RelaySound.Default;
    public string? CustomWavPath { get; init; }

    public RelaySettings Normalize()
    {
        string body = (BodyText ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (body.Length == 0)
        {
            body = "New message";
        }
        if (body.Length > 80)
        {
            body = body[..80];
        }

        string? customPath = string.IsNullOrWhiteSpace(CustomWavPath)
            ? null
            : CustomWavPath.Trim();

        return this with
        {
            SchemaVersion = 1,
            BodyText = body,
            CustomWavPath = customPath
        };
    }

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (!IncludeStableDiscord && !IncludeDiscordCanary)
        {
            errors.Add("At least one Discord release channel must be enabled.");
        }
        if (Sound == RelaySound.CustomWav)
        {
            if (string.IsNullOrWhiteSpace(CustomWavPath))
            {
                errors.Add("Select a WAV file for the custom sound option.");
            }
            else if (!Path.IsPathRooted(CustomWavPath) ||
                     !Path.GetExtension(CustomWavPath).Equals(".wav", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add("The custom sound must be an absolute path to a WAV file.");
            }
        }
        return errors;
    }
}

public sealed record RelayStatus
{
    public string Version { get; init; } = "1.0.0";
    public int ProcessId { get; init; }
    public int SessionId { get; init; }
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset LastScanAt { get; init; }
    public DateTimeOffset? LastRelayAt { get; init; }
    public int RelayedCount { get; init; }
    public string AccessStatus { get; init; } = "Unknown";
    public string? Error { get; init; }
    public string? SettingsWarning { get; init; }
}
