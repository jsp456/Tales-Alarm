using System.IO;
using TalesAlarm.Configuration;

namespace TalesAlarm.Audio;

public sealed record AudioProbeResult(bool Success, string? ErrorMessage);

public sealed record AudioImportResult(bool Success, string? FileName, string? ErrorMessage);

public interface IAudioProbe
{
    Task<AudioProbeResult> ProbeAsync(string absolutePath, CancellationToken cancellationToken);
}

public interface IUserAudioStore
{
    Task<AudioImportResult> ImportAsync(
        string sourcePath,
        string? previousFileName,
        Func<string, Task> persistFileName,
        CancellationToken cancellationToken);

    Task RestoreDefaultAsync(
        string? previousFileName,
        Func<Task> persistDefault,
        CancellationToken cancellationToken);
}

public sealed class UserAudioStore(AppPaths paths, IAudioProbe audioProbe) : IUserAudioStore
{
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".wav",
        ".mp3",
    };

    public async Task<AudioImportResult> ImportAsync(
        string sourcePath,
        string? previousFileName,
        Func<string, Task> persistFileName,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentNullException.ThrowIfNull(persistFileName);
        cancellationToken.ThrowIfCancellationRequested();

        if (!Path.IsPathFullyQualified(sourcePath))
        {
            return new(false, null, "가져올 음원 경로가 올바르지 않습니다.");
        }

        var extension = Path.GetExtension(sourcePath).ToLowerInvariant();
        if (!AllowedExtensions.Contains(extension))
        {
            return new(false, null, "WAV 또는 MP3 파일만 가져올 수 있습니다.");
        }

        if (!File.Exists(sourcePath))
        {
            return new(false, null, "선택한 음원 파일을 찾을 수 없습니다.");
        }

        Directory.CreateDirectory(paths.AudioDirectory);
        var token = Guid.NewGuid().ToString("N");
        var temporaryPath = Path.Combine(paths.AudioDirectory, $"import-{token}.tmp");
        var candidateName = $"custom-{token}{extension}";
        var candidatePath = Path.Combine(paths.AudioDirectory, candidateName);
        var keepCandidate = false;

        try
        {
            await CopyAsync(sourcePath, temporaryPath, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, candidatePath);

            var probeResult = await audioProbe.ProbeAsync(candidatePath, cancellationToken).ConfigureAwait(false);
            if (!probeResult.Success)
            {
                return new(false, null, probeResult.ErrorMessage ?? "선택한 음원을 재생할 수 없습니다.");
            }

            try
            {
                await persistFileName(candidateName).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                return new(false, null, $"음원 설정을 저장하지 못했습니다: {exception.Message}");
            }

            keepCandidate = true;
            TryDeleteManagedFile(previousFileName, candidateName);
            return new(true, candidateName, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new(false, null, $"음원 파일을 가져오지 못했습니다: {exception.Message}");
        }
        finally
        {
            TryDeleteFile(temporaryPath);
            if (!keepCandidate)
            {
                TryDeleteFile(candidatePath);
            }
        }
    }

    public async Task RestoreDefaultAsync(
        string? previousFileName,
        Func<Task> persistDefault,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(persistDefault);
        cancellationToken.ThrowIfCancellationRequested();
        await persistDefault().ConfigureAwait(false);
        TryDeleteManagedFile(previousFileName, exceptFileName: null);
    }

    private static async Task CopyAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        await using var source = new FileStream(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            useAsync: true);
        await using var destination = new FileStream(
            destinationPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81920,
            useAsync: true);
        await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
        await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private void TryDeleteManagedFile(string? fileName, string? exceptFileName)
    {
        if (!IsManagedFileName(fileName)
            || string.Equals(fileName, exceptFileName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var fullAudioDirectory = Path.GetFullPath(paths.AudioDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var candidate = Path.GetFullPath(Path.Combine(paths.AudioDirectory, fileName!));
        if (!candidate.StartsWith(fullAudioDirectory, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        TryDeleteFile(candidate);
    }

    private static bool IsManagedFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)
            || !string.Equals(Path.GetFileName(fileName), fileName, StringComparison.Ordinal)
            || !fileName.StartsWith("custom-", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return AllowedExtensions.Contains(Path.GetExtension(fileName));
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
