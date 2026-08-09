using System.IO;
using System.Text;
using TalesAlarm.Audio;
using TalesAlarm.Configuration;
using TalesAlarm.Tests.Helpers;

namespace TalesAlarm.Tests.Audio;

public sealed class DefaultAlarmAssetTests
{
    // Break caught: the generated alarm is not the promised 1.5-second 44.1 kHz mono PCM WAV.
    [Fact]
    public void DefaultAlarmWav_HasExpectedPcmMetadataAndSampleCount()
    {
        using var stream = File.OpenRead(ProjectFiles.DefaultAlarmWav);
        using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: false);

        Assert.Equal("RIFF", Encoding.ASCII.GetString(reader.ReadBytes(4)));
        Assert.Equal(stream.Length - 8, reader.ReadUInt32());
        Assert.Equal("WAVE", Encoding.ASCII.GetString(reader.ReadBytes(4)));
        Assert.Equal("fmt ", Encoding.ASCII.GetString(reader.ReadBytes(4)));
        Assert.Equal(16u, reader.ReadUInt32());
        Assert.Equal(1, reader.ReadUInt16());
        Assert.Equal(1, reader.ReadUInt16());
        Assert.Equal(44_100u, reader.ReadUInt32());
        Assert.Equal(88_200u, reader.ReadUInt32());
        Assert.Equal(2, reader.ReadUInt16());
        Assert.Equal(16, reader.ReadUInt16());
        Assert.Equal("data", Encoding.ASCII.GetString(reader.ReadBytes(4)));
        Assert.Equal(66_150 * 2u, reader.ReadUInt32());
        Assert.Equal(66_150, (stream.Length - stream.Position) / 2);
    }

    // Break caught: the generated icon is not a one-image Windows ICO file.
    [Fact]
    public void AppIcon_HasSingleImageIcoHeader()
    {
        var header = File.ReadAllBytes(ProjectFiles.AppIcon).Take(6).ToArray();

        Assert.Equal(new byte[] { 0, 0, 1, 0, 1, 0 }, header);
    }

    // Break caught: first-run extraction writes different bytes from the embedded default alarm.
    [Fact]
    public async Task EnsureInstalledAsync_WhenMissing_ExtractsEmbeddedAlarm()
    {
        using var temporary = new TemporaryDirectory();
        var paths = new AppPaths(temporary.Path);
        var installer = new DefaultAlarmInstaller(paths);

        var installedPath = await installer.EnsureInstalledAsync(CancellationToken.None);

        Assert.Equal(Path.GetFullPath(Path.Combine(paths.AudioDirectory, "default-alarm.wav")), installedPath);
        Assert.Equal(await File.ReadAllBytesAsync(ProjectFiles.DefaultAlarmWav), await File.ReadAllBytesAsync(installedPath));
    }

    // Break caught: an existing customized or corrupt default file is trusted without checking its hash.
    [Fact]
    public async Task EnsureInstalledAsync_WhenHashDiffers_ReplacesExistingFile()
    {
        using var temporary = new TemporaryDirectory();
        var paths = new AppPaths(temporary.Path);
        Directory.CreateDirectory(paths.AudioDirectory);
        var installedPath = Path.Combine(paths.AudioDirectory, "default-alarm.wav");
        await File.WriteAllTextAsync(installedPath, "stale");
        var installer = new DefaultAlarmInstaller(paths);

        await installer.EnsureInstalledAsync(CancellationToken.None);

        Assert.Equal(await File.ReadAllBytesAsync(ProjectFiles.DefaultAlarmWav), await File.ReadAllBytesAsync(installedPath));
    }

    // Break caught: every launch needlessly rewrites an already correct default alarm.
    [Fact]
    public async Task EnsureInstalledAsync_WhenHashMatches_LeavesExistingFileUntouched()
    {
        using var temporary = new TemporaryDirectory();
        var paths = new AppPaths(temporary.Path);
        Directory.CreateDirectory(paths.AudioDirectory);
        var installedPath = Path.Combine(paths.AudioDirectory, "default-alarm.wav");
        File.Copy(ProjectFiles.DefaultAlarmWav, installedPath);
        var sentinel = new DateTime(2020, 1, 2, 3, 4, 6, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(installedPath, sentinel);
        var installer = new DefaultAlarmInstaller(paths);

        await installer.EnsureInstalledAsync(CancellationToken.None);

        Assert.Equal(sentinel, File.GetLastWriteTimeUtc(installedPath));
    }
}
