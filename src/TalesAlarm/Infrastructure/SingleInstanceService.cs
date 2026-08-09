using System.IO;
using System.IO.Pipes;
using System.Text;

namespace TalesAlarm.Infrastructure;

public sealed class SingleInstanceService : IAsyncDisposable
{
    private readonly string pipeName;
    private readonly Mutex mutex;
    private readonly bool ownsInstance;
    private readonly CancellationTokenSource shutdown = new();
    private readonly TaskCompletionSource serverReady = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object sync = new();
    private Task? serverTask;
    private bool disposed;

    public SingleInstanceService(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name.IndexOfAny(['\\', '/']) >= 0)
        {
            throw new ArgumentException("인스턴스 이름에는 경로 구분자를 사용할 수 없습니다.", nameof(name));
        }

        pipeName = $"{name}.Activate";
        mutex = new Mutex(false, $"Local\\{name}.Mutex", out ownsInstance);
    }

    public event EventHandler? ActivationRequested;

    public async Task<bool> TryAcquireAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!ownsInstance)
        {
            return false;
        }

        lock (sync)
        {
            serverTask ??= RunServerLoopAsync(shutdown.Token);
        }

        await serverReady.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task SignalOwnerAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));
        await using var client = new NamedPipeClientStream(
            ".",
            pipeName,
            PipeDirection.Out,
            PipeOptions.Asynchronous);
        await client.ConnectAsync(timeout.Token).ConfigureAwait(false);
        await using var writer = new StreamWriter(
            client,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            bufferSize: 1024,
            leaveOpen: true)
        {
            AutoFlush = true,
        };
        await writer.WriteLineAsync("SHOW".AsMemory(), timeout.Token).ConfigureAwait(false);
        await writer.FlushAsync(timeout.Token).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        Task? task;
        lock (sync)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            shutdown.Cancel();
            task = serverTask;
        }

        if (task is not null)
        {
            try
            {
                await task.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
        }

        try
        {
            mutex.Dispose();
        }
        catch (ObjectDisposedException)
        {
        }

        shutdown.Dispose();
    }

    private async Task RunServerLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await using var server = new NamedPipeServerStream(
                    pipeName,
                    PipeDirection.In,
                    maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                serverReady.TrySetResult();

                try
                {
                    await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                    using var reader = new StreamReader(
                        server,
                        new UTF8Encoding(
                            encoderShouldEmitUTF8Identifier: false,
                            throwOnInvalidBytes: true),
                        detectEncodingFromByteOrderMarks: false,
                        bufferSize: 1024,
                        leaveOpen: true);
                    var message = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                    if (string.Equals(message, "SHOW", StringComparison.Ordinal))
                    {
                        RaiseActivationRequested();
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (IOException) when (!cancellationToken.IsCancellationRequested)
                {
                }
                catch (DecoderFallbackException) when (!cancellationToken.IsCancellationRequested)
                {
                }
            }
        }
        catch (Exception exception)
        {
            serverReady.TrySetException(exception);
            throw;
        }
        finally
        {
            serverReady.TrySetCanceled(cancellationToken);
        }
    }

    private void RaiseActivationRequested()
    {
        try
        {
            ActivationRequested?.Invoke(this, EventArgs.Empty);
        }
        catch
        {
        }
    }
}
