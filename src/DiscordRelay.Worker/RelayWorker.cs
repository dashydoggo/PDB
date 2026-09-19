using System.Diagnostics;
using DashyDen.PDB.Infrastructure;
using DashyDen.DiscordRelay;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace DashyDen.DiscordRelay.Worker;

internal sealed class RelayWorker
{
    private const int LowLatencyPollMilliseconds = 25;
    private readonly DateTimeOffset _startedAt = DateTimeOffset.Now;
    private DateTimeOffset _lastScanAt = DateTimeOffset.Now;
    private DateTimeOffset? _lastRelayAt;
    private int _relayedCount;
    private DateTime _settingsWriteTimeUtc = DateTime.MinValue;
    private RelaySettings _settings = new();
    private string? _settingsWarning;

    internal async Task<int> RunAsync()
    {
        ReloadSettings(force: true);
        UserNotificationListener listener = UserNotificationListener.Current;
        UserNotificationListenerAccessStatus access = listener.GetAccessStatus();
        if (access == UserNotificationListenerAccessStatus.Unspecified)
        {
            access = await listener.RequestAccessAsync();
        }

        WorkerLog.Write($"Worker started; version={Program.Version}; session={Process.GetCurrentProcess().SessionId}; access={access}.");
        if (access != UserNotificationListenerAccessStatus.Allowed)
        {
            SaveStatus(access.ToString(), "Notification access is required.");
            return 2;
        }

        using var notificationSignal = new SemaphoreSlim(0, 1);
        void SignalNotification()
        {
            try
            {
                notificationSignal.Release();
            }
            catch (SemaphoreFullException)
            {
            }
        }

        try
        {
            listener.NotificationChanged += (_, eventArgs) =>
            {
                if (eventArgs.ChangeKind == UserNotificationChangedKind.Added)
                {
                    SignalNotification();
                }
            };
            WorkerLog.Write("Notification listener event mode enabled.");
        }
        catch (Exception exception)
        {
            WorkerLog.Write("Notification listener event mode unavailable. " +
                WorkerLog.SafeException(exception));
        }

        FileSystemWatcher? databaseWatcher = null;
        try
        {
            string notificationDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Microsoft",
                "Windows",
                "Notifications");
            databaseWatcher = new FileSystemWatcher(notificationDirectory, "wpndatabase.db*")
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                IncludeSubdirectories = false,
                EnableRaisingEvents = true
            };
            databaseWatcher.Changed += (_, _) => SignalNotification();
            databaseWatcher.Created += (_, _) => SignalNotification();
            databaseWatcher.Renamed += (_, _) => SignalNotification();
            WorkerLog.Write("Notification database event mode enabled.");
        }
        catch (Exception exception)
        {
            databaseWatcher?.Dispose();
            databaseWatcher = null;
            WorkerLog.Write("Notification database event mode unavailable; using 25 ms scans. " +
                WorkerLog.SafeException(exception));
        }

        IReadOnlyList<UserNotification> initial =
            await listener.GetNotificationsAsync(NotificationKinds.Toast);
        var observedIds = new HashSet<uint>(initial.Select(notification => notification.Id));
        WorkerLog.Write($"Initial notification snapshot completed; count={initial.Count}.");
        SaveStatus(access.ToString(), null);
        DateTimeOffset nextStatusWrite = DateTimeOffset.Now.AddSeconds(30);
        int consecutiveErrors = 0;

        while (true)
        {
            await notificationSignal.WaitAsync(LowLatencyPollMilliseconds).ConfigureAwait(false);
            try
            {
                ReloadSettings(force: false);
                IReadOnlyList<UserNotification> notifications =
                    await listener.GetNotificationsAsync(NotificationKinds.Toast);
                _lastScanAt = DateTimeOffset.Now;
                consecutiveErrors = 0;

                foreach (UserNotification notification in notifications)
                {
                    if (!observedIds.Add(notification.Id) || !IsDiscordSource(notification))
                    {
                        continue;
                    }
                    if ((_lastScanAt - notification.CreationTime).TotalSeconds > 30 || !_settings.Enabled)
                    {
                        continue;
                    }

                    string title = GetPrivateTitle(notification);
                    string? avatarPath = _settings.ShowSenderAvatar
                        ? AvatarResolver.TryRecover(notification.Id, ProductPaths.DataDirectory)
                        : null;
                    string? activationUri = _settings.OpenDiscordOnClick
                        ? AvatarResolver.TryRecoverActivationUri(notification.Id)
                        : null;
                    ToastService.Show(
                        title,
                        _settings.BodyText,
                        avatarPath,
                        activationUri,
                        _settings,
                        "current");
                    _lastRelayAt = DateTimeOffset.Now;
                    _relayedCount++;
                    WorkerLog.Write(
                        $"Relayed Discord notification; id={notification.Id}; " +
                        $"titleLength={title.Length}; avatar={(avatarPath is null ? "unavailable" : "local")}; " +
                        $"activation={(activationUri is not null ? "exact" : _settings.OpenDirectMessagesWhenLinkUnavailable ? "fallback" : "disabled")}; sound={_settings.Sound}.");
                    SaveStatus(access.ToString(), null);
                }

                if (DateTimeOffset.Now >= nextStatusWrite)
                {
                    SaveStatus(access.ToString(), null);
                    nextStatusWrite = DateTimeOffset.Now.AddSeconds(30);
                }
            }
            catch (Exception exception)
            {
                consecutiveErrors++;
                string error = WorkerLog.SafeException(exception);
                WorkerLog.Write($"Polling error {consecutiveErrors}: {error}");
                SaveStatus("Error", error);
                await Task.Delay(Math.Min(10_000, consecutiveErrors * 1_000)).ConfigureAwait(false);
            }
        }
    }

    private bool IsDiscordSource(UserNotification notification)
    {
        string appId = notification.AppInfo?.AppUserModelId ?? string.Empty;
        return (_settings.IncludeStableDiscord &&
                appId.Equals("com.squirrel.Discord.Discord", StringComparison.OrdinalIgnoreCase)) ||
               (_settings.IncludeDiscordCanary &&
                appId.Equals("com.squirrel.DiscordCanary.DiscordCanary", StringComparison.OrdinalIgnoreCase));
    }

    private string GetPrivateTitle(UserNotification notification)
    {
        if (_settings.TitleMode == RelayTitleMode.DiscordOnly)
        {
            return "Discord";
        }

        try
        {
            NotificationBinding? binding = notification.Notification.Visual.GetBinding(
                KnownNotificationBindings.ToastGeneric);
            IReadOnlyList<AdaptiveNotificationText>? text = binding?.GetTextElements();
            if (text is null || text.Count < 2)
            {
                return "Discord";
            }

            string title = (text[0].Text ?? string.Empty)
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault()?.Trim() ?? string.Empty;
            if (title.Length == 0)
            {
                return "Discord";
            }
            return title.Length <= 128 ? title : title[..128];
        }
        catch
        {
            return "Discord";
        }
    }

    private void ReloadSettings(bool force)
    {
        DateTime writeTime = SettingsStore.GetLastWriteTimeUtc();
        if (!force && writeTime == _settingsWriteTimeUtc)
        {
            return;
        }

        SettingsLoadResult loaded = SettingsStore.Load();
        _settings = loaded.Settings;
        _settingsWarning = loaded.Warning;
        _settingsWriteTimeUtc = SettingsStore.GetLastWriteTimeUtc();
        WorkerLog.Write("Settings loaded; warning=" + (_settingsWarning ?? "none") + ".");
    }

    private void SaveStatus(string accessStatus, string? error)
    {
        try
        {
            SettingsStore.SaveStatus(new RelayStatus
            {
                Version = Program.Version,
                ProcessId = Environment.ProcessId,
                SessionId = Process.GetCurrentProcess().SessionId,
                StartedAt = _startedAt,
                LastScanAt = _lastScanAt,
                LastRelayAt = _lastRelayAt,
                RelayedCount = _relayedCount,
                AccessStatus = accessStatus,
                Error = error,
                SettingsWarning = _settingsWarning
            });
        }
        catch
        {
        }
    }
}
