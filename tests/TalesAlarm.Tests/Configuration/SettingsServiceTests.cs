using System.IO;
using System.Text.Json;
using TalesAlarm.Configuration;
using TalesAlarm.Tests.Helpers;
using TalesAlarm.Timers;

namespace TalesAlarm.Tests.Configuration;

public sealed class SettingsServiceTests
{
    // Break caught: changing persistence to numeric enums or failing to restore a saved setting breaks the durable settings contract.
    [Fact]
    public async Task SaveThenLoad_RoundTripsEnumsAsReadableStrings()
    {
        using var temp = new TemporaryDirectory();
        var paths = new AppPaths(temp.Path);
        var service = new SettingsService(paths, TimeProvider.System);
        var defaults = AppSettings.CreateDefault();
        var expected = defaults with
        {
            Timer1 = defaults.Timer1 with
            {
                DurationSeconds = 75,
                ReactivationPolicy = ReactivationPolicy.PauseResume,
            },
        };

        await service.SaveAsync(expected, CancellationToken.None);
        var result = await service.LoadAsync(CancellationToken.None);

        Assert.Equal(expected, result.Settings);
        Assert.Null(result.RecoveryMessage);
        Assert.Null(result.BackupPath);
        Assert.Contains("PauseResume", await File.ReadAllTextAsync(paths.SettingsFile));
        Assert.False(File.Exists(paths.SettingsTemporaryFile));
    }

    // Break caught: treating malformed JSON as usable leaves startup unable to recover from an interrupted or manually edited settings file.
    [Fact]
    public async Task Load_CorruptJsonBacksUpFileAndReturnsDefaults()
    {
        using var temp = new TemporaryDirectory();
        var paths = new AppPaths(temp.Path);
        await File.WriteAllTextAsync(paths.SettingsFile, "{ broken");
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 8, 9, 12, 0, 0, TimeSpan.Zero));
        var service = new SettingsService(paths, time);

        var result = await service.LoadAsync(CancellationToken.None);

        Assert.Equal(AppSettings.CreateDefault(), result.Settings);
        Assert.NotNull(result.RecoveryMessage);
        Assert.True(File.Exists(result.BackupPath));
        Assert.False(File.Exists(paths.SettingsFile));
    }

    // Break caught: accepting syntactically valid settings outside timer limits lets unsupported values enter the application.
    [Fact]
    public async Task Load_InvalidSettingsBacksUpFileAndReturnsDefaults()
    {
        using var temp = new TemporaryDirectory();
        var paths = new AppPaths(temp.Path);
        var invalid = AppSettings.CreateDefault() with
        {
            Timer1 = AppSettings.CreateDefault().Timer1 with { DurationSeconds = 0 },
        };
        await File.WriteAllTextAsync(paths.SettingsFile, JsonSerializer.Serialize(invalid));
        var service = new SettingsService(paths, TimeProvider.System);

        var result = await service.LoadAsync(CancellationToken.None);

        Assert.Equal(AppSettings.CreateDefault(), result.Settings);
        Assert.NotNull(result.RecoveryMessage);
        Assert.True(File.Exists(result.BackupPath));
        Assert.False(File.Exists(paths.SettingsFile));
    }

    // Break caught: writing during a pre-cancelled save can replace a known-good settings file with a partial or unintended document.
    [Fact]
    public async Task Save_PreCancelled_PreservesExistingSettingsFile()
    {
        using var temp = new TemporaryDirectory();
        var paths = new AppPaths(temp.Path);
        const string originalJson = "{\"original\":true}";
        await File.WriteAllTextAsync(paths.SettingsFile, originalJson);
        var service = new SettingsService(paths, TimeProvider.System);
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => service.SaveAsync(AppSettings.CreateDefault(), cancellationSource.Token));

        Assert.Equal(originalJson, await File.ReadAllTextAsync(paths.SettingsFile));
        Assert.False(File.Exists(paths.SettingsTemporaryFile));
    }

    // Break caught: persisting invalid settings bypasses the same safety constraints enforced for loaded documents.
    [Fact]
    public async Task Save_InvalidSettings_ThrowsValidationErrorsWithoutWritingAFile()
    {
        using var temp = new TemporaryDirectory();
        var paths = new AppPaths(temp.Path);
        var defaults = AppSettings.CreateDefault();
        var invalid = defaults with { Timer1 = defaults.Timer1 with { DurationSeconds = 0 } };
        var service = new SettingsService(paths, TimeProvider.System);

        var exception = await Assert.ThrowsAsync<SettingsValidationException>(
            () => service.SaveAsync(invalid, CancellationToken.None));

        Assert.Contains(exception.Errors, error => error.Field == "Timer1.DurationSeconds");
        Assert.False(File.Exists(paths.SettingsFile));
    }

    // Break caught: a missing settings file should not make first launch an error or a recovery event.
    [Fact]
    public async Task Load_NoSettingsFile_ReturnsDefaultsWithoutRecovery()
    {
        using var temp = new TemporaryDirectory();
        var paths = new AppPaths(temp.Path);
        var service = new SettingsService(paths, TimeProvider.System);

        var result = await service.LoadAsync(CancellationToken.None);

        Assert.Equal(AppSettings.CreateDefault(), result.Settings);
        Assert.Null(result.RecoveryMessage);
        Assert.Null(result.BackupPath);
        Assert.True(Directory.Exists(paths.RootDirectory));
    }

    // Break caught: accepting a relative command-line root puts application data under a process-dependent location.
    [Fact]
    public void FromArguments_OnlyUsesAbsoluteOverrideWhenAllowed()
    {
        using var temp = new TemporaryDirectory();
        var absolute = AppPaths.FromArguments(["--data-root", temp.Path], allowDataRootOverride: true);
        var relative = AppPaths.FromArguments(["--data-root", "relative-data"], allowDataRootOverride: true);
        var disabled = AppPaths.FromArguments(["--data-root", temp.Path], allowDataRootOverride: false);
        var defaultRoot = AppPaths.ForCurrentUser().RootDirectory;

        Assert.Equal(System.IO.Path.GetFullPath(temp.Path), absolute.RootDirectory);
        Assert.Equal(defaultRoot, relative.RootDirectory);
        Assert.Equal(defaultRoot, disabled.RootDirectory);
    }
}
