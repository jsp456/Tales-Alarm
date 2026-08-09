using System.IO;
using System.Security.Cryptography;
using TalesAlarm.Configuration;

namespace TalesAlarm.Audio;

public interface IDefaultAlarmInstaller
{
    Task<string> EnsureInstalledAsync(CancellationToken cancellationToken);
}

public sealed class DefaultAlarmInstaller(AppPaths paths) : IDefaultAlarmInstaller
{
    private const string ResourceName = "TalesAlarm.Assets.default-alarm.wav";

    public async Task<string> EnsureInstalledAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(paths.AudioDirectory);
        var destination = Path.GetFullPath(Path.Combine(paths.AudioDirectory, "default-alarm.wav"));
        var embeddedBytes = await ReadEmbeddedBytesAsync(cancellationToken).ConfigureAwait(false);
        var embeddedHash = SHA256.HashData(embeddedBytes);

        if (File.Exists(destination))
        {
            await using var existing = new FileStream(
                destination,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);
            var existingHash = await SHA256.HashDataAsync(existing, cancellationToken).ConfigureAwait(false);
            if (existingHash.AsSpan().SequenceEqual(embeddedHash))
            {
                return destination;
            }
        }

        var temporary = Path.Combine(
            paths.AudioDirectory,
            $"default-alarm.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllBytesAsync(temporary, embeddedBytes, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }

        return destination;
    }

    private static async Task<byte[]> ReadEmbeddedBytesAsync(CancellationToken cancellationToken)
    {
        await using var resource = typeof(DefaultAlarmInstaller).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException("내장 기본 알람 음원을 찾지 못했습니다.");
        await using var memory = new MemoryStream();
        await resource.CopyToAsync(memory, cancellationToken).ConfigureAwait(false);
        return memory.ToArray();
    }
}
