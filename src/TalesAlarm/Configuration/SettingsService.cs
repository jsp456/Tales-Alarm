using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TalesAlarm.Configuration;

public interface ISettingsService
{
    Task<SettingsLoadResult> LoadAsync(CancellationToken cancellationToken);

    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken);
}

public sealed record SettingsLoadResult(
    AppSettings Settings,
    string? RecoveryMessage,
    string? BackupPath);

public sealed class SettingsValidationException(
    IReadOnlyList<SettingsValidationError> errors) : Exception("설정값이 올바르지 않습니다.")
{
    public IReadOnlyList<SettingsValidationError> Errors { get; } = errors;
}

public sealed class SettingsService(AppPaths paths, TimeProvider timeProvider) : ISettingsService
{
    private const string RecoveryMessage = "설정 파일이 손상되어 기본값으로 복구했습니다.";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public async Task<SettingsLoadResult> LoadAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(paths.RootDirectory);

        if (!File.Exists(paths.SettingsFile))
            return new(AppSettings.CreateDefault(), null, null);

        try
        {
            await using var stream = new FileStream(
                paths.SettingsFile,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);
            var settings = await JsonSerializer.DeserializeAsync<AppSettings>(
                stream,
                JsonOptions,
                cancellationToken);
            if (settings is null)
                throw new JsonException("Settings document is empty.");

            var errors = SettingsValidator.Validate(settings);
            if (errors.Count > 0)
                throw new SettingsValidationException(errors);

            return new(settings, null, null);
        }
        catch (JsonException)
        {
            return RecoverFromInvalidSettings();
        }
        catch (SettingsValidationException)
        {
            return RecoverFromInvalidSettings();
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        var errors = SettingsValidator.Validate(settings);
        if (errors.Count > 0)
            throw new SettingsValidationException(errors);

        Directory.CreateDirectory(paths.RootDirectory);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            await using (var stream = new FileStream(
                paths.SettingsTemporaryFile,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None))
            {
                await JsonSerializer.SerializeAsync(stream, settings, JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (File.Exists(paths.SettingsFile))
                File.Replace(paths.SettingsTemporaryFile, paths.SettingsFile, null, ignoreMetadataErrors: true);
            else
                File.Move(paths.SettingsTemporaryFile, paths.SettingsFile);
        }
        finally
        {
            if (File.Exists(paths.SettingsTemporaryFile))
                File.Delete(paths.SettingsTemporaryFile);
        }
    }

    private SettingsLoadResult RecoverFromInvalidSettings()
    {
        var timestamp = timeProvider.GetUtcNow().ToString("yyyyMMddHHmmssfff");
        var backupPath = Path.Combine(paths.RootDirectory, $"settings.corrupt-{timestamp}.json");
        File.Move(paths.SettingsFile, backupPath);
        return new(AppSettings.CreateDefault(), RecoveryMessage, backupPath);
    }
}
