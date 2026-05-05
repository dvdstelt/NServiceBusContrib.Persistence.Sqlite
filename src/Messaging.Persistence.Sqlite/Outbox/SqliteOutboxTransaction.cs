namespace Messaging.Persistence.Sqlite.Outbox;

using Microsoft.Data.Sqlite;
using NServiceBus.Outbox;

sealed class SqliteOutboxTransaction(SqliteConnection connection, SqliteTransaction transaction) : IOutboxTransaction
{
    public SqliteConnection Connection { get; } = connection;
    public SqliteTransaction Transaction { get; } = transaction;

    public Task Commit(CancellationToken cancellationToken = default) => Transaction.CommitAsync(cancellationToken);

    public void Dispose()
    {
        Transaction.Dispose();
        Connection.Dispose();
    }

#pragma warning disable PS0018 // CancellationToken cannot be added to the IAsyncDisposable.DisposeAsync contract
    public async ValueTask DisposeAsync()
#pragma warning restore PS0018
    {
        await Transaction.DisposeAsync().ConfigureAwait(false);
        await Connection.DisposeAsync().ConfigureAwait(false);
    }
}
