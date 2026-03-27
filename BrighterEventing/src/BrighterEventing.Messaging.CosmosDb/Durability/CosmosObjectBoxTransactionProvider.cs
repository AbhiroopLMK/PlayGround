using Paramore.Brighter;

namespace BrighterEventing.Messaging.CosmosDb.Durability;

/// <summary>
/// No-op transaction provider for <see cref="CosmosBrighterOutbox"/> (<c>IAmAnOutbox&lt;Message, object&gt;</c>).
/// Cosmos writes are not coordinated with an ambient DB transaction; Brighter still requires a matching
/// <see cref="IAmABoxTransactionProvider{T}"/> so <see cref="Paramore.Brighter.CommandProcessor.GetTransactionTypeFromTransactionProvider"/> resolves to <see cref="object"/>.
/// </summary>
public sealed class CosmosObjectBoxTransactionProvider : IAmABoxTransactionProvider<object>
{
    private static readonly object Sentinel = new();
    private bool _open;

    public void Close() => _open = false;

    public void Commit() => _open = false;

    public Task CommitAsync(CancellationToken cancellationToken = default)
    {
        _open = false;
        return Task.CompletedTask;
    }

    public bool HasOpenTransaction => _open;

    public bool IsSharedConnection => false;

    public void Rollback() => _open = false;

    public Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        _open = false;
        return Task.CompletedTask;
    }

    public object GetTransaction()
    {
        _open = true;
        return Sentinel;
    }

    public Task<object> GetTransactionAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(GetTransaction());
}
