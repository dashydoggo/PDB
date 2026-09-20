using System.Diagnostics;
using DashyDen.DiscordRelay;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.Storage.Pickers;
using Windows.UI.Notifications.Management;
using WinRT.Interop;

namespace DashyDen.DiscordRelay.SettingsApp;

public sealed partial class MainWindow : Window
{
    private readonly DispatcherTimer _statusTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private DateTimeOffset? _workerStartAttemptedAt;
    private readonly IReadOnlyList<Choice<RelaySound>> _soundChoices =
    [
        new(RelaySound.Default, "Default Windows sound"),
        new(RelaySound.InstantMessage, "Instant message"),
        new(RelaySound.Mail, "New mail"),
        new(RelaySound.Reminder, "Reminder"),
        new(RelaySound.Sms, "SMS"),
        new(RelaySound.Silent, "Silent"),
        new(RelaySound.CustomWav, "Custom WAV file")
    ];
    private readonly IReadOnlyList<Choice<RelayTitleMode>> _titleChoices =
    [
        new(RelayTitleMode.SenderAndChannel, "Sender and channel when available"),
        new(RelayTitleMode.DiscordOnly, "Always show Discord")
    ];

    public MainWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        SystemBackdrop = new MicaBackdrop();
        SoundCombo.ItemsSource = _soundChoices;
        TitleModeCombo.ItemsSource = _titleChoices;
        RootGrid.Loaded += MainWindow_Loaded;
        Closed += MainWindow_Closed;
        _statusTimer.Tick += (_, _) => RefreshStatus();
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        ResizeWindow();
        try
        {
            string settingsExecutable = Environment.ProcessPath
                ?? throw new InvalidOperationException("PDB executable path is unavailable.");
            string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "pdb.png");
            NotificationIdentityRegistration.Ensure(settingsExecutable, iconPath);
            if (StartupRegistration.EnsureWorkerRunning())
            {
                _workerStartAttemptedAt = DateTimeOffset.Now;
            }
        }
        catch (Exception exception)
        {
            ShowResult(InfoBarSeverity.Warning, "Windows integration", exception.Message);
        }
        LoadSettings();
        RefreshStatus();
        _statusTimer.Start();
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        _statusTimer.Stop();
    }

    private void ResizeWindow()
    {
        IntPtr handle = WindowNative.GetWindowHandle(this);
        WindowId id = Win32Interop.GetWindowIdFromWindow(handle);
        AppWindow.GetFromWindowId(id).Resize(new SizeInt32(820, 900));
    }

    private void LoadSettings()
    {
        SettingsLoadResult loaded = SettingsStore.Load();
        RelaySettings settings = loaded.Settings;
        RelayEnabledToggle.IsOn = settings.Enabled;
        StartupToggle.IsOn = settings.StartWithWindows;
        OpenExactLinksToggle.IsOn = settings.OpenDiscordOnClick;
        OpenDirectMessagesToggle.IsOn = settings.OpenDirectMessagesWhenLinkUnavailable;
        StableToggle.IsOn = settings.IncludeStableDiscord;
        CanaryToggle.IsOn = settings.IncludeDiscordCanary;
        AvatarToggle.IsOn = settings.ShowSenderAvatar;
        HistoryToggle.IsOn = settings.KeepInNotificationCenter;
        BodyTextBox.Text = settings.BodyText;
        CustomSoundPathBox.Text = settings.CustomWavPath ?? string.Empty;
        SoundCombo.SelectedItem = _soundChoices.First(choice => choice.Value == settings.Sound);
        TitleModeCombo.SelectedItem = _titleChoices.First(choice => choice.Value == settings.TitleMode);
        UpdateCustomSoundVisibility();

        if (loaded.Warning is not null)
        {
            ShowResult(InfoBarSeverity.Warning, "Settings warning", loaded.Warning);
        }
    }

    private RelaySettings ReadSettings()
    {
        RelaySound sound = (SoundCombo.SelectedItem as Choice<RelaySound>)?.Value ?? RelaySound.Default;
        RelayTitleMode titleMode = (TitleModeCombo.SelectedItem as Choice<RelayTitleMode>)?.Value
            ?? RelayTitleMode.SenderAndChannel;
        return new RelaySettings
        {
            Enabled = RelayEnabledToggle.IsOn,
            StartWithWindows = StartupToggle.IsOn,
            OpenDiscordOnClick = OpenExactLinksToggle.IsOn,
            OpenDirectMessagesWhenLinkUnavailable = OpenDirectMessagesToggle.IsOn,
            IncludeStableDiscord = StableToggle.IsOn,
            IncludeDiscordCanary = CanaryToggle.IsOn,
            ShowSenderAvatar = AvatarToggle.IsOn,
            KeepInNotificationCenter = HistoryToggle.IsOn,
            TitleMode = titleMode,
            BodyText = BodyTextBox.Text,
            Sound = sound,
            CustomWavPath = string.IsNullOrWhiteSpace(CustomSoundPathBox.Text)
                ? null
                : CustomSoundPathBox.Text
        }.Normalize();
    }

    private bool SaveSettings()
    {
        try
        {
            RelaySettings settings = ReadSettings();
            IReadOnlyList<string> errors = settings.Validate();
            if (errors.Count > 0)
            {
                ShowResult(InfoBarSeverity.Error, "Cannot save settings", errors[0]);
                return false;
            }

            SettingsStore.Save(settings);
            StartupRegistration.Apply(settings.StartWithWindows);
            ShowResult(InfoBarSeverity.Success, "Settings saved", "The worker reloads changes automatically.");
            return true;
        }
        catch (Exception exception)
        {
            ShowResult(InfoBarSeverity.Error, "Cannot save settings", exception.Message);
            return false;
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        SaveSettings();
    }

    private void TestNotification_Click(object sender, RoutedEventArgs e)
    {
        if (!SaveSettings())
        {
            return;
        }

        string? workerPath = StartupRegistration.FindWorkerExecutable();
        if (workerPath is null)
        {
            ShowResult(InfoBarSeverity.Error, "Worker not found", "Install or publish the worker beside the settings application.");
            return;
        }

        Process.Start(new ProcessStartInfo(workerPath, "--test-notification")
        {
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(workerPath)
        });
    }

    private async void BrowseSound_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.MusicLibrary,
            ViewMode = PickerViewMode.List
        };
        picker.FileTypeFilter.Add(".wav");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        Windows.Storage.StorageFile? file = await picker.PickSingleFileAsync();
        if (file is not null)
        {
            CustomSoundPathBox.Text = file.Path;
        }
    }

    private async void GrantAccess_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            UserNotificationListenerAccessStatus status =
                await UserNotificationListener.Current.RequestAccessAsync();
            ShowResult(
                status == UserNotificationListenerAccessStatus.Allowed
                    ? InfoBarSeverity.Success
                    : InfoBarSeverity.Warning,
                "Notification access",
                status.ToString());
            RefreshStatus();
        }
        catch (Exception exception)
        {
            ShowResult(InfoBarSeverity.Error, "Access request failed", exception.Message);
        }
    }

    private void OpenNotificationSettings_Click(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo("ms-settings:notifications") { UseShellExecute = true });
    }

    private void SoundCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateCustomSoundVisibility();
    }

    private void UpdateCustomSoundVisibility()
    {
        RelaySound sound = (SoundCombo.SelectedItem as Choice<RelaySound>)?.Value ?? RelaySound.Default;
        CustomSoundPanel.Visibility = sound == RelaySound.CustomWav
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void RefreshStatus()
    {
        RelayStatus? status = SettingsStore.LoadStatus();
        if (status is null)
        {
            bool starting = _workerStartAttemptedAt is not null &&
                            DateTimeOffset.Now - _workerStartAttemptedAt.Value < TimeSpan.FromSeconds(10);
            StatusInfoBar.Severity = starting
                ? InfoBarSeverity.Informational
                : InfoBarSeverity.Warning;
            StatusInfoBar.Message = starting ? "Starting PDB worker…" : "PDB worker is not running.";
            DetailsText.Text = string.Empty;
            return;
        }

        bool healthy = status.AccessStatus.Equals("Allowed", StringComparison.OrdinalIgnoreCase) &&
                       status.Error is null &&
                       DateTimeOffset.Now - status.LastScanAt < TimeSpan.FromMinutes(2);
        StatusInfoBar.Severity = healthy ? InfoBarSeverity.Success : InfoBarSeverity.Warning;
        StatusInfoBar.Message = healthy
            ? $"Running. {status.RelayedCount} notification(s) relayed."
            : status.Error ?? $"Notification access: {status.AccessStatus}";
        DetailsText.Text = $"Version {status.Version} · Last scan {status.LastScanAt.LocalDateTime:G}" +
            (status.LastRelayLatencyMilliseconds is null
                ? string.Empty
                : $" · {status.LastRelayLatencyMilliseconds} ms");
    }

    private void ShowResult(InfoBarSeverity severity, string title, string message)
    {
        SaveInfoBar.Severity = severity;
        SaveInfoBar.Title = title;
        SaveInfoBar.Message = message;
        SaveInfoBar.IsOpen = true;
    }

    private sealed record Choice<T>(T Value, string Label)
    {
        public override string ToString() => Label;
    }
}
