namespace Messaging.Persistence.Sqlite;

using Messaging.Persistence.Sqlite.Outbox;
using Microsoft.Data.Sqlite;
using NServiceBus.Extensibility;
using NServiceBus.Outbox;
using NServiceBus.Persistence;
using NServiceBus.Transport;

sealed class SqliteSynchronizedStorageSession(IConnectionFactory connectionFactory)
    : ICompletableSynchronizedStorageSession, ISqliteStorageSession
{
    SqliteConnection? connection;
    SqliteTransaction? transaction;
    bool ownsConnection;
    bool committed;

    public SqliteConnection Connection =>
        connection ?? throw new InvalidOperationException("The synchronized storage session has not been opened.");

    public SqliteTransaction Transaction =>
        transaction ?? throw new InvalidOperationException("The synchronized storage session has not been opened.");

    public ValueTask<bool> TryOpen(IOutboxTransaction outboxTransaction, ContextBag context, CancellationToken cancellationToken = default)
    {
        if (outboxTransaction is SqliteOutboxTransaction sqliteOutbox)
        {
            connection = sqliteOutbox.Connection;
            transaction = sqliteOutbox.Transaction;
            ownsConnection = false;
            return new ValueTask<bool>(true);
        }
        return new ValueTask<bool>(false);
    }

    public ValueTask<bool> TryOpen(TransportTransaction transportTransaction, ContextBag context, CancellationToken cancellationToken = default)
        => new(false);

    public async Task Open(ContextBag contextBag, CancellationToken cancellationToken = default)
    {
        if (connection is not null)
        {
            return;
        }

        connection = await connectionFactory.OpenConnection(cancellationToken).ConfigureAwait(false);
        transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        ownsConnection = true;
    }

    public async Task CompleteAsync(CancellationToken cancellationToken = default)
    {
        if (committed || !ownsConnection || transaction is null)
        {
            return;
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        committed = true;
    }

    public void Dispose()
    {
        if (ownsConnection)
        {
            transaction?.Dispose();
            connection?.Dispose();
        }
        transaction = null;
        connection = null;
    }

#pragma warning disable PS0018 // CancellationToken cannot be added to the IAsyncDisposable.DisposeAsync contract
    public ValueTask DisposeAsync()
#pragma warning restore PS0018
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
