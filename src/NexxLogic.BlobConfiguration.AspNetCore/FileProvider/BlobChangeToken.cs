using System.Threading;
using Microsoft.Extensions.Primitives;

namespace NexxLogic.BlobConfiguration.AspNetCore.FileProvider;

internal class BlobChangeToken : IChangeToken, IDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private int _disposed; // 0 = false, 1 = true
    public bool ActiveChangeCallbacks => _disposed == 0;
    public bool HasChanged => _cts.IsCancellationRequested;
    public CancellationToken CancellationToken => _cts.Token;

    public IDisposable RegisterChangeCallback(Action<object?> callback, object? state)
    {
        return _disposed == 1
            ? throw new ObjectDisposedException(nameof(BlobChangeToken)) 
            : _cts.Token.Register(callback, state);
    }

    public void OnReload() 
    {
        if (_disposed == 0)
        {
            _cts.Cancel();
        }
    }

    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) == 1)
        {
            return; // Already disposed
        }
        
        _cts.Cancel();
        _cts.Dispose();
    }
}