using System.Globalization;
using System.IO;
using System.Text;
using TalesAlarm.Configuration;

namespace TalesAlarm.Infrastructure;

public sealed class FileLogger(AppPaths paths, TimeProvider timeProvider)
{
    private static readonly object Sync = new();
    private static readonly UTF8Encoding Utf8WithoutBom = new(
        encoderShouldEmitUTF8Identifier: false);

    public void Write(string message, Exception? exception = null)
    {
        ArgumentNullException.ThrowIfNull(message);
        lock (Sync)
        {
            try
            {
                Directory.CreateDirectory(paths.LogsDirectory);
                var now = timeProvider.GetLocalNow();
                var logPath = Path.Combine(paths.LogsDirectory, $"app-{now:yyyyMMdd}.log");
                var entry = new StringBuilder()
                    .Append('[')
                    .Append(now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture))
                    .Append("] ")
                    .AppendLine(message);
                if (exception is not null)
                {
                    entry.AppendLine(exception.ToString());
                }

                File.AppendAllText(logPath, entry.ToString(), Utf8WithoutBom);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    public void PruneOldLogs()
    {
        lock (Sync)
        {
            try
            {
                if (!Directory.Exists(paths.LogsDirectory))
                {
                    return;
                }

                var today = DateOnly.FromDateTime(timeProvider.GetLocalNow().DateTime);
                var oldestRetainedDate = today.AddDays(-7);
                foreach (var filePath in Directory.EnumerateFiles(paths.LogsDirectory, "app-*.log"))
                {
                    var fileName = Path.GetFileName(filePath);
                    if (fileName.Length != "app-yyyyMMdd.log".Length
                        || !DateOnly.TryParseExact(
                            fileName.AsSpan(4, 8),
                            "yyyyMMdd",
                            CultureInfo.InvariantCulture,
                            DateTimeStyles.None,
                            out var fileDate)
                        || fileDate >= oldestRetainedDate)
                    {
                        continue;
                    }

                    try
                    {
                        File.Delete(filePath);
                    }
                    catch (IOException)
                    {
                    }
                    catch (UnauthorizedAccessException)
                    {
                    }
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
