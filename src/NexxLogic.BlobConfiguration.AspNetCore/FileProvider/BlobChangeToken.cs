using Microsoft.Extensions.Primitives;

namespace NexxLogic.BlobConfiguration.AspNetCore.FileProvider;

internal class BlobChangeToken : IChangeToken
{
    private CancellationTokenSource _cts = new();

    public bool ActiveChangeCallbacks => true;
    public bool HasChanged => _cts.IsCancellationRequested;
    public CancellationToken CancellationToken => _cts.Token;

    public IDisposable RegisterChangeCallback(Action<object?> callback, object? state)
        => _cts.Token.Register(callback, state);

    public void OnReload() => _cts.Cancel();
}