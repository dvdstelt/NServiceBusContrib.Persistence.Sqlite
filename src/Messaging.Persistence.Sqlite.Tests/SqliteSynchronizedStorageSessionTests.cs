namespace Messaging.Persistence.Sqlite.Tests;

using Messaging.Persistence.Sqlite.Outbox;
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
}
