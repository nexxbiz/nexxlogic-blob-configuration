using Microsoft.Extensions.Primitives;

namespace NexxLogic.BlobConfiguration.AspNetCore.FileProvider;

internal class BlobChangeToken : IChangeToken, IDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private int _disposed; // 0 = false, 1 = true
    public bool ActiveChangeCallbacks => Volatile.Read(ref _disposed) == 0;
    public bool HasChanged => _cts.IsCancellationRequested;
    public CancellationToken CancellationToken => _cts.Token;

    public IDisposable RegisterChangeCallback(Action<object?> callback, object? state)
    {
        if (Volatile.Read(ref _disposed) == 1)
        {
            throw new ObjectDisposedException(nameof(BlobChangeToken));
        }
        
        try
        {
            return _cts.Token.Register(callback, state);
        }
        catch (ObjectDisposedException)
        {
            // CTS was disposed between our check and the Register call
            throw new ObjectDisposedException(nameof(BlobChangeToken));
        }
    }

    public void OnReload() 
    {
        if (Volatile.Read(ref _disposed) == 0)
        {
            try
            {
                _cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // CTS was disposed between our check and the Cancel call - safe to ignore
            }
        }
    }

    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) == 1)
        {
            return; // Already disposed
        }
        
        try
        {
            _cts.Cancel();
            _cts.Dispose();
        }
        catch (ObjectDisposedException)
        {
            // CTS was already disposed - safe to ignore
        }
    }
}