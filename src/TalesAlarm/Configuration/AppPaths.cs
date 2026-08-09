using System.IO;

namespace TalesAlarm.Configuration;

public sealed record AppPaths(string RootDirectory)
{
    public string SettingsFile => Path.Combine(RootDirectory, "settings.json");

    public string SettingsTemporaryFile => Path.Combine(RootDirectory, "settings.tmp");

    public string AudioDirectory => Path.Combine(RootDirectory, "Audio");

    public string LogsDirectory => Path.Combine(RootDirectory, "Logs");

    public static AppPaths ForCurrentUser() => new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TalesAlarm"));

    public static AppPaths FromArguments(
        IReadOnlyList<string> args,
        bool allowDataRootOverride)
    {
        if (allowDataRootOverride)
        {
            for (var index = 0; index < args.Count - 1; index++)
            {
                if (args[index] == "--data-root" && Path.IsPathFullyQualified(args[index + 1]))
                    return new(Path.GetFullPath(args[index + 1]));
            }
        }

        return ForCurrentUser();
    }
}
