using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace CaptureCoyote.App.Services;

public sealed class SingleInstanceService : IDisposable
{
    private readonly Mutex _mutex;
    private readonly string _pipeName;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private bool _disposed;

    public SingleInstanceService(string appId)
    {
        _mutex = new Mutex(initiallyOwned: true, $@"Local\{appId}", out var createdNew);
        _pipeName = $"{appId}.Activation";
        IsPrimaryInstance = createdNew;
    }

    public bool IsPrimaryInstance { get; }

    public event Func<string[], Task>? ActivationRequested;

    public void StartListening()
    {
        if (!IsPrimaryInstance)
        {
            return;
        }

        _ = ListenLoopAsync(_cancellationTokenSource.Token);
    }

    public async Task<bool> SignalPrimaryInstanceAsync(string[] args, CancellationToken cancellationToken = default)
    {
        for (var attempt = 0; attempt < 6; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                using var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.Out, PipeOptions.Asynchronous);
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(TimeSpan.FromMilliseconds(500));

                await client.ConnectAsync(timeoutCts.Token).ConfigureAwait(false);
                var payload = JsonSerializer.Serialize(args ?? Array.Empty<string>());
                await using var writer = new StreamWriter(client, new UTF8Encoding(false), leaveOpen: false);
                await writer.WriteAsync(payload).ConfigureAwait(false);
                await writer.FlushAsync().ConfigureAwait(false);
                return true;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }

            await Task.Delay(150, cancellationToken).ConfigureAwait(false);
        }

        return false;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cancellationTokenSource.Cancel();
        _cancellationTokenSource.Dispose();

        if (IsPrimaryInstance)
        {
            _mutex.ReleaseMutex();
        }

        _mutex.Dispose();
    }

    private async Task ListenLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(
                    _pipeName,
                    PipeDirection.In,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                using var reader = new StreamReader(server, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: false);
                var payload = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(payload))
                {
                    continue;
                }

                var args = JsonSerializer.Deserialize<string[]>(payload) ?? Array.Empty<string>();
                var activationRequested = ActivationRequested;
                if (activationRequested is not null)
                {
                    await activationRequested.Invoke(args).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
            }
        }
    }
}
