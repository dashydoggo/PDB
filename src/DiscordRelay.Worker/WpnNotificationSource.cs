using System.Runtime.InteropServices;
using System.Text;
using System.Xml;
using DashyDen.PDB.Infrastructure;

namespace DashyDen.DiscordRelay.Worker;

internal sealed class WpnSourceNotification
{
    internal uint Id { get; init; }
    internal string AppId { get; init; } = string.Empty;
    internal DateTimeOffset CreationTime { get; init; }
    internal string Title { get; init; } = "Discord";
    internal string? AvatarPath { get; init; }
    internal string? ActivationUri { get; init; }
}

internal sealed class WpnNotificationSource
{
    private const int SqliteOk = 0;
    private const int SqliteRow = 100;
    private const int SqliteDone = 101;
    private const int SqliteOpenReadOnly = 0x00000001;
    private const int SqliteOpenFullMutex = 0x00010000;
    private const string StableAppId = "com.squirrel.Discord.Discord";
    private const string CanaryAppId = "com.squirrel.DiscordCanary.DiscordCanary";
    private const string LatestIdQuery =
        "SELECT COALESCE(MAX(n.Id), 0) " +
        "FROM Notification n " +
        "INNER JOIN NotificationHandler h ON h.RecordId = n.HandlerId " +
        "WHERE h.PrimaryId IN ('" + StableAppId + "', '" + CanaryAppId + "')";
    private const string NewNotificationsQuery =
        "SELECT n.Id, h.PrimaryId, n.Payload, n.ArrivalTime " +
        "FROM Notification n " +
        "INNER JOIN NotificationHandler h ON h.RecordId = n.HandlerId " +
        "WHERE n.Id > ?1 " +
        "AND h.PrimaryId IN ('" + StableAppId + "', '" + CanaryAppId + "') " +
        "AND n.PayloadType = 'Xml' " +
        "ORDER BY n.Id";

    private readonly string _databasePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Microsoft",
        "Windows",
        "Notifications",
        "wpndatabase.db");
    private long _lastId;

    internal bool TryInitialize()
    {
        IntPtr database = IntPtr.Zero;
        IntPtr statement = IntPtr.Zero;
        try
        {
            if (!TryOpen(out database) || !TryPrepare(database, LatestIdQuery, out statement))
            {
                return false;
            }

            if (NativeSqlite.sqlite3_step(statement) != SqliteRow)
            {
                return false;
            }
            _lastId = NativeSqlite.sqlite3_column_int64(statement, 0);
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (statement != IntPtr.Zero)
            {
                NativeSqlite.sqlite3_finalize(statement);
            }
            if (database != IntPtr.Zero)
            {
                NativeSqlite.sqlite3_close(database);
            }
        }
    }

    internal bool TryReadNew(RelaySettings settings, out IReadOnlyList<WpnSourceNotification> notifications)
    {
        var result = new List<WpnSourceNotification>();
        notifications = result;
        IntPtr database = IntPtr.Zero;
        IntPtr statement = IntPtr.Zero;
        long maximumId = _lastId;
        try
        {
            if (!TryOpen(out database) || !TryPrepare(database, NewNotificationsQuery, out statement))
            {
                return false;
            }
            if (NativeSqlite.sqlite3_bind_int64(statement, 1, _lastId) != SqliteOk)
            {
                return false;
            }

            int stepResult;
            while ((stepResult = NativeSqlite.sqlite3_step(statement)) == SqliteRow)
            {
                long rawId = NativeSqlite.sqlite3_column_int64(statement, 0);
                maximumId = Math.Max(maximumId, rawId);
                if (rawId <= 0 || rawId > uint.MaxValue)
                {
                    continue;
                }

                string appId = ReadText(statement, 1);
                if (!settings.Enabled || !IsEnabledSource(appId, settings))
                {
                    continue;
                }

                byte[]? payload = ReadBlob(statement, 2);
                if (payload is null || payload.Length == 0)
                {
                    continue;
                }

                long arrivalFileTime = NativeSqlite.sqlite3_column_int64(statement, 3);
                var details = AvatarResolver.TryRecoverDetailsFromPayload(
                    (uint)rawId,
                    ProductPaths.DataDirectory,
                    payload,
                    settings.ShowSenderAvatar,
                    settings.OpenDiscordOnClick);
                result.Add(new WpnSourceNotification
                {
                    Id = (uint)rawId,
                    AppId = appId,
                    CreationTime = ParseCreationTime(arrivalFileTime),
                    Title = GetPrivateTitle(payload, settings.TitleMode),
                    AvatarPath = details.AvatarPath,
                    ActivationUri = details.ActivationUri
                });
            }

            if (stepResult != SqliteDone)
            {
                return false;
            }
            _lastId = maximumId;
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (statement != IntPtr.Zero)
            {
                NativeSqlite.sqlite3_finalize(statement);
            }
            if (database != IntPtr.Zero)
            {
                NativeSqlite.sqlite3_close(database);
            }
        }
    }

    private bool TryOpen(out IntPtr database)
    {
        database = IntPtr.Zero;
        if (!File.Exists(_databasePath))
        {
            return false;
        }
        int result = NativeSqlite.sqlite3_open_v2(
            Utf8(_databasePath),
            out database,
            SqliteOpenReadOnly | SqliteOpenFullMutex,
            IntPtr.Zero);
        if (result != SqliteOk || database == IntPtr.Zero)
        {
            return false;
        }
        NativeSqlite.sqlite3_busy_timeout(database, 25);
        return true;
    }

    private static bool TryPrepare(IntPtr database, string sql, out IntPtr statement)
    {
        statement = IntPtr.Zero;
        return NativeSqlite.sqlite3_prepare_v2(
            database,
            Utf8(sql),
            -1,
            out statement,
            IntPtr.Zero) == SqliteOk && statement != IntPtr.Zero;
    }

    private static bool IsEnabledSource(string appId, RelaySettings settings)
    {
        return (settings.IncludeStableDiscord &&
                appId.Equals(StableAppId, StringComparison.OrdinalIgnoreCase)) ||
               (settings.IncludeDiscordCanary &&
                appId.Equals(CanaryAppId, StringComparison.OrdinalIgnoreCase));
    }

    private static byte[]? ReadBlob(IntPtr statement, int column)
    {
        int length = NativeSqlite.sqlite3_column_bytes(statement, column);
        IntPtr source = NativeSqlite.sqlite3_column_blob(statement, column);
        if (length <= 0 || source == IntPtr.Zero)
        {
            return null;
        }
        var value = new byte[length];
        Marshal.Copy(source, value, 0, length);
        return value;
    }

    private static string ReadText(IntPtr statement, int column)
    {
        IntPtr value = NativeSqlite.sqlite3_column_text(statement, column);
        return value == IntPtr.Zero ? string.Empty : Marshal.PtrToStringAnsi(value) ?? string.Empty;
    }

    private static DateTimeOffset ParseCreationTime(long fileTime)
    {
        try
        {
            return new DateTimeOffset(DateTime.FromFileTimeUtc(fileTime));
        }
        catch
        {
            return DateTimeOffset.Now;
        }
    }

    private static string GetPrivateTitle(byte[] payload, RelayTitleMode mode)
    {
        if (mode == RelayTitleMode.DiscordOnly)
        {
            return "Discord";
        }

        try
        {
            string xmlText = payload.Length >= 2 && payload[0] == 0xFF && payload[1] == 0xFE
                ? Encoding.Unicode.GetString(payload)
                : Encoding.UTF8.GetString(payload);
            xmlText = xmlText.Trim('\0', '\uFEFF', ' ', '\r', '\n', '\t');
            var document = new XmlDocument { XmlResolver = null };
            document.LoadXml(xmlText);
            XmlNodeList? textNodes = document.SelectNodes("//text");
            if (textNodes is null || textNodes.Count < 2)
            {
                return "Discord";
            }

            string title = (textNodes[0]?.InnerText ?? string.Empty)
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

    private static byte[] Utf8(string value)
    {
        return Encoding.UTF8.GetBytes(value + "\0");
    }
}
