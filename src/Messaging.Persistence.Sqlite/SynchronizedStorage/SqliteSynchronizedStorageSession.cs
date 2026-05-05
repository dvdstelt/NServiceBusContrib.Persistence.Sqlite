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

        var openedConnection = await connectionFactory.OpenConnection(cancellationToken).ConfigureAwait(false);
        try
        {
            // BEGIN DEFERRED so two sessions can read concurrently. Optimistic-concurrency conflicts
            // are detected by the WHERE-clause version check on saga UPDATE/DELETE.
            // Note: BeginTransactionAsync(IsolationLevel.ReadCommitted) maps to BEGIN IMMEDIATE in
            // Microsoft.Data.Sqlite (it reassigns ReadCommitted to Serializable internally), so we
            // use the sync BeginTransaction(deferred: true) overload instead. Sync is fine here
            // since SQLite BEGIN does no I/O.
            transaction = openedConnection.BeginTransaction(deferred: true);
        }
        catch
        {
            await openedConnection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
        connection = openedConnection;
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
    public async ValueTask DisposeAsync()
#pragma warning restore PS0018
    {
        if (ownsConnection)
        {
            if (transaction is not null)
            {
                await transaction.DisposeAsync().ConfigureAwait(false);
            }
            if (connection is not null)
            {
                await connection.DisposeAsync().ConfigureAwait(false);
            }
        }
        transaction = null;
        connection = null;
    }
}
