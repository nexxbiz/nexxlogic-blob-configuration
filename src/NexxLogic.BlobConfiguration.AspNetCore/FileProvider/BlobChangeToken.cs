using Microsoft.Extensions.Primitives;

namespace NexxLogic.BlobConfiguration.AspNetCore.FileProvider;

internal class BlobChangeToken : IChangeToken, IDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private volatile bool _disposed;
    public bool ActiveChangeCallbacks => !_disposed;
    public bool HasChanged => _cts.IsCancellationRequested;
    public CancellationToken CancellationToken => _cts.Token;

    public IDisposable RegisterChangeCallback(Action<object?> callback, object? state)
    {
        return _disposed 
            ? throw new ObjectDisposedException(nameof(BlobChangeToken)) 
            : _cts.Token.Register(callback, state);
    }

    public void OnReload() 
    {
        if (!_disposed)
        {
            _cts.Cancel();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        
        _cts.Cancel();
        _cts.Dispose();
    }
}