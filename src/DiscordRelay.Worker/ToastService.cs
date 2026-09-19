using DashyDen.DiscordRelay;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace DashyDen.DiscordRelay.Worker;

internal static class ToastService
{
    private const string ToastGroup = "discord-sanitized";
    private const int TransientLifetimeSeconds = 12;

    internal static void Show(
        string title,
        string body,
        string? avatarPath,
        string? activationUri,
        RelaySettings settings,
        string tag)
    {
        var xml = new XmlDocument();
        xml.LoadXml(
            "<toast><visual><binding template=\"ToastGeneric\"><text/><text/></binding></visual></toast>");
        string? launchUri = null;
        if (settings.OpenDiscordOnClick && !string.IsNullOrWhiteSpace(activationUri))
        {
            launchUri = activationUri;
        }
        else if (string.IsNullOrWhiteSpace(activationUri) &&
                 settings.OpenDirectMessagesWhenLinkUnavailable)
        {
            launchUri = "discord://-/channels/@me";
        }
        if (launchUri is not null)
        {
            XmlElement toastElement = xml.DocumentElement;
            toastElement.SetAttribute("activationType", "protocol");
            toastElement.SetAttribute("launch", launchUri);
        }
        XmlNodeList textNodes = xml.GetElementsByTagName("text");
        textNodes[0].AppendChild(xml.CreateTextNode(title));
        textNodes[1].AppendChild(xml.CreateTextNode(body));
        IXmlNode binding = xml.GetElementsByTagName("binding")[0];

        if (!string.IsNullOrWhiteSpace(avatarPath) && File.Exists(avatarPath))
        {
            XmlElement image = xml.CreateElement("image");
            image.SetAttribute("placement", "appLogoOverride");
            image.SetAttribute("hint-crop", "circle");
            image.SetAttribute("src", new Uri(avatarPath).AbsoluteUri);
            binding.AppendChild(image);
        }

        AddAudio(xml, settings.Sound);
        var toast = new ToastNotification(xml)
        {
            Tag = tag.Length <= 64 ? tag : tag[..64],
            Group = ToastGroup,
            SuppressPopup = false
        };

        if (!settings.KeepInNotificationCenter)
        {
            toast.ExpirationTime = DateTimeOffset.Now.AddSeconds(TransientLifetimeSeconds);
        }

        ToastNotificationManager.CreateToastNotifier(ProductPaths.AppUserModelId).Show(toast);
        if (settings.Sound == RelaySound.CustomWav)
        {
            NativeMethods.PlayCustomWav(settings.CustomWavPath);
        }

        if (!settings.KeepInNotificationCenter)
        {
            _ = RemoveLaterAsync(toast.Tag);
        }
    }

    private static void AddAudio(XmlDocument xml, RelaySound sound)
    {
        if (sound == RelaySound.Default)
        {
            return;
        }

        XmlElement audio = xml.CreateElement("audio");
        string? eventUri = sound switch
        {
            RelaySound.InstantMessage => "ms-winsoundevent:Notification.IM",
            RelaySound.Mail => "ms-winsoundevent:Notification.Mail",
            RelaySound.Reminder => "ms-winsoundevent:Notification.Reminder",
            RelaySound.Sms => "ms-winsoundevent:Notification.SMS",
            _ => null
        };
        if (eventUri is null)
        {
            audio.SetAttribute("silent", "true");
        }
        else
        {
            audio.SetAttribute("src", eventUri);
        }
        xml.DocumentElement.AppendChild(audio);
    }

    private static async Task RemoveLaterAsync(string tag)
    {
        await Task.Delay(TimeSpan.FromSeconds(TransientLifetimeSeconds)).ConfigureAwait(false);
        try
        {
            ToastNotificationManager.History.Remove(tag, ToastGroup, ProductPaths.AppUserModelId);
        }
        catch
        {
        }
    }
}
