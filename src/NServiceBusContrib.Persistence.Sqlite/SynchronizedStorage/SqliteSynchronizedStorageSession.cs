namespace NServiceBusContrib.Persistence.Sqlite;

using NServiceBusContrib.Persistence.Sqlite.Outbox;
using Microsoft.Data.Sqlite;
using NServiceBus.Extensibility;
using NServiceBus.Outbox;
using NServiceBus.Persistence;
using NServiceBus.Transport;

sealed class SqliteSynchronizedStorageSession(IConnectionFactory connectionFactory)
    : ICompletableSynchronizedStorageSession, ISqliteStorageSession
{
    State state = State.Closed.Instance;

    public SqliteConnection Connection => state.GetConnectionOrThrow();

    public SqliteTransaction Transaction => state.GetTransactionOrThrow();

    public SqliteCommand CreateCommand()
    {
        var command = Connection.CreateCommand();
        command.Transaction = Transaction;
        return command;
    }

    public ValueTask<bool> TryOpen(IOutboxTransaction outboxTransaction, ContextBag context, CancellationToken cancellationToken = default)
    {
        if (outboxTransaction is SqliteOutboxTransaction sqliteOutbox)
        {
            state = new State.Borrowed(sqliteOutbox.Connection, sqliteOutbox.Transaction);
            return new ValueTask<bool>(true);
        }
        return new ValueTask<bool>(false);
    }

    public ValueTask<bool> TryOpen(TransportTransaction transportTransaction, ContextBag context, CancellationToken cancellationToken = default)
        => new(false);

    public async Task Open(ContextBag contextBag, CancellationToken cancellationToken = default)
    {
        if (state is not State.Closed)
        {
            return;
        }

        var openedConnection = await connectionFactory.OpenConnection(cancellationToken).ConfigureAwait(false);
        SqliteTransaction openedTransaction;
        try
        {
            // BEGIN DEFERRED so two sessions can read concurrently. Optimistic-concurrency conflicts
            // are detected by the WHERE-clause version check on saga UPDATE/DELETE.
            // Note: BeginTransactionAsync(IsolationLevel.ReadCommitted) maps to BEGIN IMMEDIATE in
            // Microsoft.Data.Sqlite (it reassigns ReadCommitted to Serializable internally), so we
            // use the sync BeginTransaction(deferred: true) overload instead. Sync is fine here
            // since SQLite BEGIN does no I/O.
            openedTransaction = openedConnection.BeginTransaction(deferred: true);
        }
        catch
        {
            await openedConnection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
        state = new State.Owned(openedConnection, openedTransaction, Committed: false);
    }

    public async Task CompleteAsync(CancellationToken cancellationToken = default)
    {
        if (state is not State.Owned { Committed: false } owned)
        {
            return;
        }

        await owned.Transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        state = owned with { Committed = true };
    }

    public void Dispose()
    {
        if (state is State.Owned owned)
        {
            owned.Transaction.Dispose();
            owned.Connection.Dispose();
        }
        state = State.Closed.Instance;
    }

#pragma warning disable PS0018 // CancellationToken cannot be added to the IAsyncDisposable.DisposeAsync contract
    public async ValueTask DisposeAsync()
#pragma warning restore PS0018
    {
        if (state is State.Owned owned)
        {
            await owned.Transaction.DisposeAsync().ConfigureAwait(false);
            await owned.Connection.DisposeAsync().ConfigureAwait(false);
        }
        state = State.Closed.Instance;
    }

    abstract record State
    {
        public abstract SqliteConnection GetConnectionOrThrow();
        public abstract SqliteTransaction GetTransactionOrThrow();

        public sealed record Closed : State
        {
            public static readonly Closed Instance = new();

            public override SqliteConnection GetConnectionOrThrow() =>
                throw new InvalidOperationException("The synchronized storage session has not been opened.");
            public override SqliteTransaction GetTransactionOrThrow() =>
                throw new InvalidOperationException("The synchronized storage session has not been opened.");
        }

        public sealed record Borrowed(SqliteConnection Connection, SqliteTransaction Transaction) : State
        {
            public override SqliteConnection GetConnectionOrThrow() => Connection;
            public override SqliteTransaction GetTransactionOrThrow() => Transaction;
        }

        public sealed record Owned(SqliteConnection Connection, SqliteTransaction Transaction, bool Committed) : State
        {
            public override SqliteConnection GetConnectionOrThrow() => Connection;
            public override SqliteTransaction GetTransactionOrThrow() => Transaction;
        }
    }
}
