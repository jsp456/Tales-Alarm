using System.IO;

namespace TalesAlarm.Tests.Helpers;

internal static class ProjectFiles
{
    public static string RepositoryRoot { get; } = FindRepositoryRoot();

    public static string DefaultAlarmWav => Path.Combine(
        RepositoryRoot,
        "src",
        "TalesAlarm",
        "Assets",
        "default-alarm.wav");

    public static string AppIcon => Path.Combine(
        RepositoryRoot,
        "src",
        "TalesAlarm",
        "Assets",
        "tales-alarm.ico");

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TalesAlarm.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("TalesAlarm.sln이 있는 저장소 루트를 찾지 못했습니다.");
    }
}
