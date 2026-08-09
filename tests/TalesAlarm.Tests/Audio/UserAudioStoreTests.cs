using System.IO;
using TalesAlarm.Audio;
using TalesAlarm.Configuration;
using TalesAlarm.Tests.Helpers;

namespace TalesAlarm.Tests.Audio;

public sealed class UserAudioStoreTests
{
    // Break caught: persistence runs before a usable managed copy exists, or the old file is deleted too early.
    [Fact]
    public async Task ImportAsync_CopiesBeforePersistAndDeletesOldOnlyAfterSuccess()
    {
        using var temporary = new TemporaryDirectory();
        var paths = new AppPaths(temporary.Path);
        Directory.CreateDirectory(paths.AudioDirectory);
        var oldName = "custom-old.wav";
        var oldPath = Path.Combine(paths.AudioDirectory, oldName);
        await File.WriteAllBytesAsync(oldPath, [1, 2]);
        var source = Path.Combine(temporary.Path, "picked.wav");
        await File.WriteAllBytesAsync(source, [3, 4, 5]);
        var probe = new FakeAudioProbe(success: true);
        var store = new UserAudioStore(paths, probe);
        string? persistedName = null;

        var result = await store.ImportAsync(
            source,
            oldName,
            name =>
            {
                Assert.True(File.Exists(Path.Combine(paths.AudioDirectory, name)));
                Assert.True(File.Exists(oldPath));
                persistedName = name;
                return Task.CompletedTask;
            },
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(result.FileName, persistedName);
        Assert.True(File.Exists(Path.Combine(paths.AudioDirectory, result.FileName!)));
        Assert.False(File.Exists(oldPath));
        Assert.Equal(Path.Combine(paths.AudioDirectory, result.FileName!), Assert.Single(probe.ProbedPaths));
        AssertNoTemporaryCandidates(paths);
    }

    // Break caught: case-sensitive extension checks reject valid MP3 selections from Windows.
    [Fact]
    public async Task ImportAsync_WithUppercaseMp3_AcceptsAndNormalizesExtension()
    {
        using var temporary = new TemporaryDirectory();
        var paths = new AppPaths(temporary.Path);
        var source = Path.Combine(temporary.Path, "picked.MP3");
        await File.WriteAllBytesAsync(source, [1, 2, 3]);
        var store = new UserAudioStore(paths, new FakeAudioProbe(success: true));

        var result = await store.ImportAsync(source, null, _ => Task.CompletedTask, CancellationToken.None);

        Assert.True(result.Success);
        Assert.EndsWith(".mp3", result.FileName, StringComparison.Ordinal);
    }

    // Break caught: unsupported files reach the media probe or leave partial managed files behind.
    [Fact]
    public async Task ImportAsync_WithUnsupportedExtension_PreservesOldFileAndCreatesNothing()
    {
        using var temporary = new TemporaryDirectory();
        var (paths, oldName, source) = await ArrangeOldAndSourceAsync(temporary, "picked.txt");
        var probe = new FakeAudioProbe(success: true);
        var store = new UserAudioStore(paths, probe);

        var result = await store.ImportAsync(source, oldName, _ => Task.CompletedTask, CancellationToken.None);

        Assert.False(result.Success);
        Assert.True(File.Exists(Path.Combine(paths.AudioDirectory, oldName)));
        Assert.Empty(probe.ProbedPaths);
        AssertNoCandidates(paths, oldName);
    }

    // Break caught: a candidate rejected by the media probe remains on disk or replaces the old setting.
    [Fact]
    public async Task ImportAsync_WhenProbeFails_PreservesOldFileAndRemovesCandidate()
    {
        using var temporary = new TemporaryDirectory();
        var (paths, oldName, source) = await ArrangeOldAndSourceAsync(temporary, "picked.wav");
        var store = new UserAudioStore(paths, new FakeAudioProbe(success: false, "읽을 수 없음"));
        var persisted = false;

        var result = await store.ImportAsync(
            source,
            oldName,
            _ => { persisted = true; return Task.CompletedTask; },
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("읽을 수 없음", result.ErrorMessage);
        Assert.False(persisted);
        Assert.True(File.Exists(Path.Combine(paths.AudioDirectory, oldName)));
        AssertNoCandidates(paths, oldName);
    }

    // Break caught: a settings-save failure strands the candidate and deletes the still-selected old file.
    [Fact]
    public async Task ImportAsync_WhenPersistFails_PreservesOldFileAndRemovesCandidate()
    {
        using var temporary = new TemporaryDirectory();
        var (paths, oldName, source) = await ArrangeOldAndSourceAsync(temporary, "picked.wav");
        var store = new UserAudioStore(paths, new FakeAudioProbe(success: true));

        var result = await store.ImportAsync(
            source,
            oldName,
            _ => throw new IOException("설정 저장 실패"),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("설정 저장 실패", result.ErrorMessage);
        Assert.True(File.Exists(Path.Combine(paths.AudioDirectory, oldName)));
        AssertNoCandidates(paths, oldName);
    }

    // Break caught: cancellation leaves import-*.tmp or custom-* candidates and removes the old file.
    [Fact]
    public async Task ImportAsync_WhenCancelled_PreservesOldFileAndRemovesCandidates()
    {
        using var temporary = new TemporaryDirectory();
        var (paths, oldName, source) = await ArrangeOldAndSourceAsync(temporary, "picked.wav");
        var store = new UserAudioStore(paths, new FakeAudioProbe(success: true));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            store.ImportAsync(source, oldName, _ => Task.CompletedTask, cancellation.Token));

        Assert.True(File.Exists(Path.Combine(paths.AudioDirectory, oldName)));
        AssertNoCandidates(paths, oldName);
    }

    // Break caught: failed default-setting persistence deletes the custom file still referenced by settings.
    [Fact]
    public async Task RestoreDefaultAsync_WhenPersistFails_PreservesPreviousManagedFile()
    {
        using var temporary = new TemporaryDirectory();
        var paths = new AppPaths(temporary.Path);
        Directory.CreateDirectory(paths.AudioDirectory);
        var oldName = "custom-old.wav";
        var oldPath = Path.Combine(paths.AudioDirectory, oldName);
        await File.WriteAllBytesAsync(oldPath, [1, 2]);
        var store = new UserAudioStore(paths, new FakeAudioProbe(success: true));

        await Assert.ThrowsAsync<IOException>(() => store.RestoreDefaultAsync(
            oldName,
            () => throw new IOException("설정 저장 실패"),
            CancellationToken.None));

        Assert.True(File.Exists(oldPath));
    }

    // Break caught: successful default restoration leaves an obsolete managed custom file behind.
    [Fact]
    public async Task RestoreDefaultAsync_AfterPersist_DeletesPreviousManagedFile()
    {
        using var temporary = new TemporaryDirectory();
        var paths = new AppPaths(temporary.Path);
        Directory.CreateDirectory(paths.AudioDirectory);
        var oldName = "custom-old.wav";
        var oldPath = Path.Combine(paths.AudioDirectory, oldName);
        await File.WriteAllBytesAsync(oldPath, [1, 2]);
        var store = new UserAudioStore(paths, new FakeAudioProbe(success: true));
        var persisted = false;

        await store.RestoreDefaultAsync(
            oldName,
            () => { persisted = true; return Task.CompletedTask; },
            CancellationToken.None);

        Assert.True(persisted);
        Assert.False(File.Exists(oldPath));
    }

    // Break caught: a malicious previous filename can delete a file outside the managed Audio directory.
    [Fact]
    public async Task ImportAsync_WithUnmanagedPreviousPath_NeverDeletesOutsideAudioDirectory()
    {
        using var temporary = new TemporaryDirectory();
        var paths = new AppPaths(temporary.Path);
        var outsidePath = Path.Combine(temporary.Path, "outside.wav");
        await File.WriteAllBytesAsync(outsidePath, [9]);
        var source = Path.Combine(temporary.Path, "picked.wav");
        await File.WriteAllBytesAsync(source, [1, 2, 3]);
        var store = new UserAudioStore(paths, new FakeAudioProbe(success: true));

        var result = await store.ImportAsync(
            source,
            "..\\outside.wav",
            _ => Task.CompletedTask,
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.True(File.Exists(outsidePath));
    }

    private static async Task<(AppPaths Paths, string OldName, string Source)> ArrangeOldAndSourceAsync(
        TemporaryDirectory temporary,
        string sourceName)
    {
        var paths = new AppPaths(temporary.Path);
        Directory.CreateDirectory(paths.AudioDirectory);
        const string oldName = "custom-old.wav";
        await File.WriteAllBytesAsync(Path.Combine(paths.AudioDirectory, oldName), [1, 2]);
        var source = Path.Combine(temporary.Path, sourceName);
        await File.WriteAllBytesAsync(source, [3, 4, 5]);
        return (paths, oldName, source);
    }

    private static void AssertNoCandidates(AppPaths paths, string oldName)
    {
        AssertNoTemporaryCandidates(paths);
        var customFiles = Directory.EnumerateFiles(paths.AudioDirectory, "custom-*")
            .Where(path => !string.Equals(Path.GetFileName(path), oldName, StringComparison.OrdinalIgnoreCase));
        Assert.Empty(customFiles);
    }

    private static void AssertNoTemporaryCandidates(AppPaths paths) =>
        Assert.Empty(Directory.Exists(paths.AudioDirectory)
            ? Directory.EnumerateFiles(paths.AudioDirectory, "import-*.tmp")
            : []);

    private sealed class FakeAudioProbe(bool success, string? errorMessage = null) : IAudioProbe
    {
        public List<string> ProbedPaths { get; } = [];

        public Task<AudioProbeResult> ProbeAsync(string absolutePath, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ProbedPaths.Add(absolutePath);
            return Task.FromResult(new AudioProbeResult(success, errorMessage));
        }
    }
}
