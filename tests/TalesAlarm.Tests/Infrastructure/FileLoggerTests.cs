using System.IO;
using TalesAlarm.Configuration;
using TalesAlarm.Infrastructure;
using TalesAlarm.Tests.Helpers;

namespace TalesAlarm.Tests.Infrastructure;

public sealed class FileLoggerTests
{
    // Break caught: pruning deletes the seven-day boundary or retains files older than it.
    [Fact]
    public void PruneOldLogs_DeletesOnlyFilesOlderThanSevenCalendarDays()
    {
        using var temporary = new TemporaryDirectory();
        var paths = new AppPaths(temporary.Path);
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 8, 9, 12, 0, 0, TimeSpan.Zero));
        Directory.CreateDirectory(paths.LogsDirectory);
        var today = DateOnly.FromDateTime(time.GetLocalNow().DateTime);
        var eightDaysOld = CreateLog(paths, today.AddDays(-8));
        var sevenDaysOld = CreateLog(paths, today.AddDays(-7));
        var oneDayOld = CreateLog(paths, today.AddDays(-1));
        var current = CreateLog(paths, today);
        var unrelated = Path.Combine(paths.LogsDirectory, "notes.txt");
        File.WriteAllText(unrelated, "keep");
        var logger = new FileLogger(paths, time);

        logger.PruneOldLogs();

        Assert.False(File.Exists(eightDaysOld));
        Assert.True(File.Exists(sevenDaysOld));
        Assert.True(File.Exists(oneDayOld));
        Assert.True(File.Exists(current));
        Assert.True(File.Exists(unrelated));
    }

    // Break caught: diagnostic writes omit Korean text, timestamps, or exception identity.
    [Fact]
    public void Write_AppendsUtf8TimestampMessageAndException()
    {
        using var temporary = new TemporaryDirectory();
        var paths = new AppPaths(temporary.Path);
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 8, 9, 12, 34, 56, TimeSpan.Zero));
        var logger = new FileLogger(paths, time);

        logger.Write("테스트 메시지", new InvalidOperationException("고장"));

        var today = DateOnly.FromDateTime(time.GetLocalNow().DateTime);
        var logPath = Path.Combine(paths.LogsDirectory, $"app-{today:yyyyMMdd}.log");
        var text = File.ReadAllText(logPath, System.Text.Encoding.UTF8);
        Assert.Contains("2026", text);
        Assert.Contains("테스트 메시지", text);
        Assert.Contains(nameof(InvalidOperationException), text);
        Assert.Contains("고장", text);
    }

    private static string CreateLog(AppPaths paths, DateOnly date)
    {
        var path = Path.Combine(paths.LogsDirectory, $"app-{date:yyyyMMdd}.log");
        File.WriteAllText(path, "log");
        return path;
    }
}
