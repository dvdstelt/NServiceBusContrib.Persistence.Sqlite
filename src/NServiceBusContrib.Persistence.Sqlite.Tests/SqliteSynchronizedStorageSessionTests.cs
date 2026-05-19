namespace NServiceBusContrib.Persistence.Sqlite.Tests;

using NServiceBusContrib.Persistence.Sqlite.Outbox;
using Microsoft.Data.Sqlite;
using NServiceBus.Extensibility;
using NUnit.Framework;

[TestFixture]
public class SqliteSynchronizedStorageSessionTests
{
    string dbPath = null!;
    string connectionString = null!;
    DefaultConnectionFactory factory = null!;

    [SetUp]
    public async Task SetUp()
    {
        dbPath = Path.Combine(Path.GetTempPath(), $"messaging-sqlite-tests-{Guid.NewGuid():N}.db");
        connectionString = $"Data Source={dbPath}";
        factory = new DefaultConnectionFactory(connectionString);

        await using var conn = await factory.OpenConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "CREATE TABLE T (V INTEGER NOT NULL);";
        await cmd.ExecuteNonQueryAsync();
    }

    [TearDown]
    public void TearDown()
    {
        SqliteConnection.ClearAllPools();
        try { File.Delete(dbPath); } catch { /* file may still be locked briefly */ }
    }

    [Test]
    public async Task Open_CreatesConnectionAndTransaction()
    {
        await using var session = new SqliteSynchronizedStorageSession(factory);
        await session.Open(new ContextBag());

        Assert.That(session.Connection, Is.Not.Null);
        Assert.That(session.Transaction, Is.Not.Null);
        Assert.That(session.Connection.State, Is.EqualTo(System.Data.ConnectionState.Open));
    }

    [Test]
    public void Open_BeforeOpening_ConnectionAccessThrows()
    {
        using var session = new SqliteSynchronizedStorageSession(factory);
        Assert.Throws<InvalidOperationException>(() => _ = session.Connection);
        Assert.Throws<InvalidOperationException>(() => _ = session.Transaction);
    }

    [Test]
    public async Task CompleteAsync_CommitsTransaction()
    {
        await using (var session = new SqliteSynchronizedStorageSession(factory))
        {
            await session.Open(new ContextBag());
            await Insert(session, 42);
            await session.CompleteAsync();
        }

        Assert.That(await CountRows(), Is.EqualTo(1));
    }

    [Test]
    public async Task DisposeWithoutComplete_RollsBack()
    {
        await using (var session = new SqliteSynchronizedStorageSession(factory))
        {
            await session.Open(new ContextBag());
            await Insert(session, 99);
            // No CompleteAsync; disposal must roll back.
        }

        Assert.That(await CountRows(), Is.EqualTo(0));
    }

    [Test]
    public async Task DoubleComplete_IsNoOp()
    {
        await using var session = new SqliteSynchronizedStorageSession(factory);
        await session.Open(new ContextBag());
        await Insert(session, 7);
        await session.CompleteAsync();
        Assert.DoesNotThrowAsync(() => session.CompleteAsync());
    }

    [Test]
    public async Task TryOpen_WithSqliteOutboxTransaction_BorrowsConnectionAndTransaction()
    {
        var conn = await factory.OpenConnection();
        var tx = (SqliteTransaction)await conn.BeginTransactionAsync();
        using var outboxTx = new SqliteOutboxTransaction(conn, tx);

        await using var session = new SqliteSynchronizedStorageSession(factory);
        var opened = await session.TryOpen(outboxTx, new ContextBag());

        Assert.That(opened, Is.True);
        Assert.That(session.Connection, Is.SameAs(conn));
        Assert.That(session.Transaction, Is.SameAs(tx));
    }

    [Test]
    public async Task BorrowedSession_DisposeDoesNotCloseConnection()
    {
        var conn = await factory.OpenConnection();
        var tx = (SqliteTransaction)await conn.BeginTransactionAsync();
        try
        {
            using var outboxTx = new SqliteOutboxTransaction(conn, tx);

            await using (var session = new SqliteSynchronizedStorageSession(factory))
            {
                await session.TryOpen(outboxTx, new ContextBag());
            }

            Assert.That(conn.State, Is.EqualTo(System.Data.ConnectionState.Open),
                "Borrowed connections must outlive the session that borrowed them.");
        }
        finally
        {
            tx.Dispose();
            conn.Dispose();
        }
    }

    [Test]
    public async Task TryOpen_WithUnrelatedOutboxTransaction_ReturnsFalse()
    {
        await using var session = new SqliteSynchronizedStorageSession(factory);
        var fake = new FakeOutboxTransaction();

        var opened = await session.TryOpen(fake, new ContextBag());

        Assert.That(opened, Is.False);
    }

    static async Task Insert(SqliteSynchronizedStorageSession session, int value)
    {
        await using var cmd = session.Connection.CreateCommand();
        cmd.Transaction = session.Transaction;
        cmd.CommandText = "INSERT INTO T (V) VALUES ($v);";
        cmd.Parameters.AddWithValue("$v", value);
        await cmd.ExecuteNonQueryAsync();
    }

    async Task<long> CountRows()
    {
        await using var conn = await factory.OpenConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM T;";
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    sealed class FakeOutboxTransaction : NServiceBus.Outbox.IOutboxTransaction
    {
        public Task Commit(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    [Test]
    public async Task CreateCommand_ReturnsCommandBoundToSessionTransaction()
    {
        await using var s = new SqliteSynchronizedStorageSession(factory);
        await s.Open(new ContextBag());

        await using var cmd = ((ISqliteStorageSession)s).CreateCommand();

        Assert.That(cmd.Connection, Is.SameAs(s.Connection));
        Assert.That(cmd.Transaction, Is.SameAs(s.Transaction));
    }

    [Test]
    public async Task Open_CalledTwice_IsIdempotent()
    {
        // NSB's pipeline shouldn't call Open twice, but a user-facing caller composing
        // transactional sessions might. The second call must not replace the connection
        // or open a second transaction.
        await using var s = new SqliteSynchronizedStorageSession(factory);
        await s.Open(new ContextBag());
        var connectionAfterFirstOpen = s.Connection;
        var transactionAfterFirstOpen = s.Transaction;

        await s.Open(new ContextBag());

        Assert.That(s.Connection, Is.SameAs(connectionAfterFirstOpen),
            "the second Open must not replace the connection");
        Assert.That(s.Transaction, Is.SameAs(transactionAfterFirstOpen),
            "the second Open must not replace the transaction");
    }

    [Test]
    public async Task CompleteAsync_AfterDispose_IsSilentNoOp()
    {
        var s = new SqliteSynchronizedStorageSession(factory);
        await s.Open(new ContextBag());
        await s.DisposeAsync();

        Assert.DoesNotThrowAsync(() => s.CompleteAsync(),
            "CompleteAsync after Dispose must not throw - the session is already in the Closed state");
    }

    [Test]
    public async Task DisposeAsync_CalledTwice_DoesNotThrow()
    {
        var s = new SqliteSynchronizedStorageSession(factory);
        await s.Open(new ContextBag());
        await s.DisposeAsync();

        Assert.DoesNotThrowAsync(async () => await s.DisposeAsync());
    }

    [Test]
    public async Task CompleteAsync_OnBorrowedSession_DoesNotCommitTheBorrowedTransaction()
    {
        // The owner of a borrowed transaction (the outbox) is responsible for committing it.
        // If CompleteAsync committed early, two parties would race to commit and the outbox
        // could not roll back on a downstream failure.
        var conn = await factory.OpenConnection();
        var tx = (SqliteTransaction)await conn.BeginTransactionAsync();
        try
        {
            using var outboxTx = new SqliteOutboxTransaction(conn, tx);

            var s = new SqliteSynchronizedStorageSession(factory);
            await s.TryOpen(outboxTx, new ContextBag());
            await s.CompleteAsync();
            await s.DisposeAsync();

            // The transaction should still be live - we can roll it back.
            Assert.DoesNotThrow(() => tx.Rollback());
        }
        finally
        {
            tx.Dispose();
            conn.Dispose();
        }
    }

    [Test]
    public void Open_BeginTransactionThrows_OwnedConnectionIsDisposed()
    {
        // The session calls connectionFactory.OpenConnection then BeginTransaction. If
        // BeginTransaction throws after the connection has been assigned, the connection
        // would otherwise leak: ownsConnection is still false at that point so a later
        // Dispose() does nothing.
        var poisonedFactory = new ReturnsClosedConnectionFactory();
        var session = new SqliteSynchronizedStorageSession(poisonedFactory);

        Assert.ThrowsAsync<InvalidOperationException>(() => session.Open(new ContextBag()));
        Assert.That(poisonedFactory.LastConnection, Is.Not.Null,
            "factory should have produced a connection before BeginTransaction was attempted");
        Assert.That(poisonedFactory.LastConnection!.DisposeCalls, Is.GreaterThan(0),
            "the connection must be disposed after BeginTransaction throws");
    }

    sealed class ReturnsClosedConnectionFactory : IConnectionFactory
    {
        public TrackedSqliteConnection? LastConnection { get; private set; }

        public ValueTask<SqliteConnection> OpenConnection(CancellationToken cancellationToken = default)
        {
            // Deliberately not opened: BeginTransaction on a closed connection throws
            // InvalidOperationException, simulating a transient connection failure between
            // OpenConnection() returning and BeginTransaction running.
            LastConnection = new TrackedSqliteConnection("Data Source=:memory:");
            return ValueTask.FromResult<SqliteConnection>(LastConnection);
        }
    }

    sealed class TrackedSqliteConnection(string connectionString) : SqliteConnection(connectionString)
    {
        public int DisposeCalls { get; private set; }

        protected override void Dispose(bool disposing)
        {
            DisposeCalls++;
            base.Dispose(disposing);
        }
    }
}
