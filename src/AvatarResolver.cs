using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Xml;

namespace DashyDen.PDB.Infrastructure
{
    internal static class AvatarResolver
    {
        private const int SqliteOk = 0;
        private const int SqliteRow = 100;
        private const int SqliteOpenReadOnly = 0x00000001;
        private const int SqliteOpenFullMutex = 0x00010000;
        private const int MaximumAvatarBytes = 8 * 1024 * 1024;
        private const int MaximumCachedAvatars = 256;

        private const string PayloadQuery =
            "SELECT n.Payload " +
            "FROM Notification n " +
            "INNER JOIN NotificationHandler h ON h.RecordId = n.HandlerId " +
            "WHERE n.Id = ?1 " +
            "AND h.PrimaryId IN ('com.squirrel.Discord.Discord', " +
            "'com.squirrel.DiscordCanary.DiscordCanary') " +
            "AND n.PayloadType = 'Xml' " +
            "LIMIT 1";

        internal static string TryRecover(uint notificationId, string dataDirectory)
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    byte[] payload = ReadPayload(notificationId);
                    if (payload == null || payload.Length == 0)
                    {
                        return null;
                    }

                    string sourcePath = ExtractSafeSourcePath(payload);
                    if (sourcePath == null)
                    {
                        return null;
                    }

                    string avatarDirectory = Path.Combine(dataDirectory, "avatars");
                    Directory.CreateDirectory(avatarDirectory);
                    string destinationPath = Path.Combine(
                        avatarDirectory,
                        notificationId.ToString() + ".png");
                    string temporaryPath = destinationPath + ".tmp";

                    File.Copy(sourcePath, temporaryPath, true);
                    if (File.Exists(destinationPath))
                    {
                        File.Delete(destinationPath);
                    }
                    File.Move(temporaryPath, destinationPath);
                    TrimCache(avatarDirectory);
                    return destinationPath;
                }
                catch
                {
                    if (attempt < 2)
                    {
                        Thread.Sleep(25 * (attempt + 1));
                    }
                }
            }

            return null;
        }

        internal static string TryRecoverActivationUri(uint notificationId)
        {
            try
            {
                byte[] payload = ReadPayload(notificationId);
                if (payload == null || payload.Length == 0)
                {
                    return null;
                }
                return ExtractSafeActivationUri(payload);
            }
            catch
            {
                return null;
            }
        }

        private static byte[] ReadPayload(uint notificationId)
        {
            string databasePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Microsoft",
                "Windows",
                "Notifications",
                "wpndatabase.db");
            if (!File.Exists(databasePath))
            {
                return null;
            }

            IntPtr database = IntPtr.Zero;
            IntPtr statement = IntPtr.Zero;
            try
            {
                int openResult = NativeSqlite.sqlite3_open_v2(
                    Utf8(databasePath),
                    out database,
                    SqliteOpenReadOnly | SqliteOpenFullMutex,
                    IntPtr.Zero);
                if (openResult != SqliteOk || database == IntPtr.Zero)
                {
                    return null;
                }

                NativeSqlite.sqlite3_busy_timeout(database, 100);
                int prepareResult = NativeSqlite.sqlite3_prepare_v2(
                    database,
                    Utf8(PayloadQuery),
                    -1,
                    out statement,
                    IntPtr.Zero);
                if (prepareResult != SqliteOk || statement == IntPtr.Zero)
                {
                    return null;
                }

                if (NativeSqlite.sqlite3_bind_int64(statement, 1, notificationId) != SqliteOk ||
                    NativeSqlite.sqlite3_step(statement) != SqliteRow)
                {
                    return null;
                }

                int byteCount = NativeSqlite.sqlite3_column_bytes(statement, 0);
                IntPtr blob = NativeSqlite.sqlite3_column_blob(statement, 0);
                if (byteCount <= 0 || blob == IntPtr.Zero || byteCount > 1024 * 1024)
                {
                    return null;
                }

                byte[] result = new byte[byteCount];
                Marshal.Copy(blob, result, 0, byteCount);
                return result;
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

        private static string ExtractSafeSourcePath(byte[] payload)
        {
            string xmlText;
            if (payload.Length >= 2 && payload[0] == 0xFF && payload[1] == 0xFE)
            {
                xmlText = Encoding.Unicode.GetString(payload);
            }
            else
            {
                xmlText = Encoding.UTF8.GetString(payload);
            }
            xmlText = xmlText.Trim('\0', '\uFEFF', ' ', '\r', '\n', '\t');

            var document = new XmlDocument();
            document.XmlResolver = null;
            document.LoadXml(xmlText);
            XmlElement image = document.SelectSingleNode(
                "//image[@placement='appLogoOverride']") as XmlElement;
            if (image == null)
            {
                return null;
            }

            string value = image.GetAttribute("src");
            if (String.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            string candidatePath = value;
            Uri candidateUri;
            if (Uri.TryCreate(value, UriKind.Absolute, out candidateUri) && candidateUri.IsFile)
            {
                candidatePath = candidateUri.LocalPath;
            }
            if (!Path.IsPathRooted(candidatePath))
            {
                return null;
            }

            string fullPath = Path.GetFullPath(candidatePath);
            string temporaryRoot = Path.GetFullPath(Path.GetTempPath())
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                Path.DirectorySeparatorChar;
            if (!fullPath.StartsWith(temporaryRoot, StringComparison.OrdinalIgnoreCase) ||
                !Path.GetExtension(fullPath).Equals(".png", StringComparison.OrdinalIgnoreCase) ||
                !File.Exists(fullPath))
            {
                return null;
            }

            var file = new FileInfo(fullPath);
            if ((file.Attributes & FileAttributes.ReparsePoint) != 0 ||
                file.Length <= 0 ||
                file.Length > MaximumAvatarBytes)
            {
                return null;
            }

            return fullPath;
        }

        private static string ExtractSafeActivationUri(byte[] payload)
        {
            string xmlText;
            if (payload.Length >= 2 && payload[0] == 0xFF && payload[1] == 0xFE)
            {
                xmlText = Encoding.Unicode.GetString(payload);
            }
            else
            {
                xmlText = Encoding.UTF8.GetString(payload);
            }
            xmlText = xmlText.Trim('\0', '\uFEFF', ' ', '\r', '\n', '\t');

            var document = new XmlDocument();
            document.XmlResolver = null;
            document.LoadXml(xmlText);
            var candidates = new System.Collections.Generic.List<string>();
            if (document.DocumentElement != null)
            {
                candidates.Add(document.DocumentElement.GetAttribute("launch"));
            }
            XmlNodeList actions = document.SelectNodes("//action");
            if (actions != null)
            {
                foreach (XmlNode node in actions)
                {
                    XmlElement action = node as XmlElement;
                    if (action != null)
                    {
                        candidates.Add(action.GetAttribute("arguments"));
                    }
                }
            }

            foreach (string candidate in candidates)
            {
                Uri uri;
                if (String.IsNullOrWhiteSpace(candidate) ||
                    !Uri.TryCreate(candidate, UriKind.Absolute, out uri))
                {
                    continue;
                }
                if (uri.Scheme.Equals("discord", StringComparison.OrdinalIgnoreCase))
                {
                    return uri.AbsoluteUri;
                }
                if (uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase) &&
                    (uri.Host.Equals("discord.com", StringComparison.OrdinalIgnoreCase) ||
                     uri.Host.Equals("canary.discord.com", StringComparison.OrdinalIgnoreCase) ||
                     uri.Host.Equals("ptb.discord.com", StringComparison.OrdinalIgnoreCase)) &&
                    uri.AbsolutePath.StartsWith("/channels/", StringComparison.OrdinalIgnoreCase))
                {
                    return "discord://-" + uri.PathAndQuery;
                }
            }
            return null;
        }

        private static void TrimCache(string avatarDirectory)
        {
            FileInfo[] files = new DirectoryInfo(avatarDirectory).GetFiles("*.png");
            Array.Sort(files, delegate(FileInfo left, FileInfo right)
            {
                return right.LastWriteTimeUtc.CompareTo(left.LastWriteTimeUtc);
            });
            for (int index = MaximumCachedAvatars; index < files.Length; index++)
            {
                try
                {
                    files[index].Delete();
                }
                catch
                {
                }
            }
        }

        private static byte[] Utf8(string value)
        {
            return Encoding.UTF8.GetBytes(value + "\0");
        }
    }

    internal static class NativeSqlite
    {
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_open_v2(
            byte[] filename,
            out IntPtr database,
            int flags,
            IntPtr virtualFileSystem);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_busy_timeout(IntPtr database, int milliseconds);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_prepare_v2(
            IntPtr database,
            byte[] sql,
            int byteCount,
            out IntPtr statement,
            IntPtr tail);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_bind_int64(IntPtr statement, int index, long value);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_step(IntPtr statement);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr sqlite3_column_blob(IntPtr statement, int column);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_column_bytes(IntPtr statement, int column);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_finalize(IntPtr statement);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_close(IntPtr database);
    }
}
