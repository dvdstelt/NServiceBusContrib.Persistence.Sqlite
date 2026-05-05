namespace Messaging.Persistence.Sqlite.Tests;

using Messaging.Persistence.Sqlite.Outbox;
using Microsoft.Data.Sqlite;
using NUnit.Framework;

[TestFixture]
public class OutboxCleanerTests
{
    string dbPath = null!;
    DefaultConnectionFactory factory = null!;

    [SetUp]
    public async Task SetUp()
    {
        dbPath = Path.Combine(Path.GetTempPath(), $"messaging-sqlite-cleaner-{Guid.NewGuid():N}.db");
        factory = new DefaultConnectionFactory($"Data Source={dbPath}");

        await using var conn = await factory.OpenConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = SchemaScripts.CreateOutboxTable(tablePrefix: "");
        await cmd.ExecuteNonQueryAsync();
    }

    [TearDown]
    public void TearDown()
    {
        SqliteConnection.ClearAllPools();
        try { File.Delete(dbPath); } catch { /* file may briefly remain locked */ }
    }

    const string TestEndpoint = "test-endpoint";

    [Test]
    public async Task CleanupOnce_RemovesOnlyDispatchedRecordsBeyondRetention()
    {
        var oldDispatched = DateTime.UtcNow - TimeSpan.FromDays(30);
        var recentDispatched = DateTime.UtcNow - TimeSpan.FromMinutes(5);

        await Insert("old-dispatched", dispatched: true, dispatchedAt: oldDispatched);
        await Insert("recent-dispatched", dispatched: true, dispatchedAt: recentDispatched);
        await Insert("not-dispatched", dispatched: false, dispatchedAt: null);

        var deleted = await OutboxCleaner.CleanupOnce(
            factory, tablePrefix: "", endpointName: TestEndpoint,
            retentionPeriod: TimeSpan.FromDays(7),
            batchSize: 1000, cancellationToken: CancellationToken.None);

        Assert.That(deleted, Is.EqualTo(1));

        Assert.That(await Exists("old-dispatched"), Is.False);
        Assert.That(await Exists("recent-dispatched"), Is.True);
        Assert.That(await Exists("not-dispatched"), Is.True);
    }

    [Test]
    public async Task CleanupOnce_RespectsBatchSize()
    {
        var old = DateTime.UtcNow - TimeSpan.FromDays(30);
        for (int i = 0; i < 5; i++)
        {
            await Insert($"old-{i}", dispatched: true, dispatchedAt: old);
        }

        var deleted = await OutboxCleaner.CleanupOnce(
            factory, tablePrefix: "", endpointName: TestEndpoint,
            retentionPeriod: TimeSpan.FromDays(7),
            batchSize: 2, cancellationToken: CancellationToken.None);

        Assert.That(deleted, Is.EqualTo(2));
        Assert.That(await CountRows(), Is.EqualTo(3));
    }

    [Test]
    public async Task CleanupOnce_NoDispatchedRecords_DeletesNothing()
    {
        await Insert("a", dispatched: false, dispatchedAt: null);
        await Insert("b", dispatched: false, dispatchedAt: null);

        var deleted = await OutboxCleaner.CleanupOnce(
            factory, tablePrefix: "", endpointName: TestEndpoint,
            retentionPeriod: TimeSpan.FromDays(7),
            batchSize: 1000, cancellationToken: CancellationToken.None);

        Assert.That(deleted, Is.EqualTo(0));
        Assert.That(await CountRows(), Is.EqualTo(2));
    }

    [Test]
    public async Task TryCleanupOnceAsync_SurvivesContentionWithActiveOutboxTransaction()
    {
        // SQLite serialises writers, so a cleanup DELETE will fail with SQLITE_BUSY when an
        // outbox INSERT is holding the writer lock past busy_timeout. The expectation is that
        // the contention surfaces as a SqliteException that TryCleanupOnceAsync swallows so
        // the periodic loop survives and tries again next interval - it must not throw or
        // deadlock indefinitely.

        var stale = DateTime.UtcNow - TimeSpan.FromDays(30);
        await Insert("stale-dispatched", dispatched: true, dispatchedAt: stale);

        var outboxPersister = new Messaging.Persistence.Sqlite.Outbox.SqliteOutboxPersister(
            factory, tablePrefix: "", endpointName: TestEndpoint);
        var pendingMessage = new NServiceBus.Outbox.OutboxMessage(
            "in-flight",
            [new NServiceBus.Outbox.TransportOperation(
                "op", new NServiceBus.Transport.DispatchProperties(),
                System.Text.Encoding.UTF8.GetBytes("body"),
                new Dictionary<string, string>())]);

        await using var outboxTx = await outboxPersister.BeginTransaction(new NServiceBus.Extensibility.ContextBag());
        await outboxPersister.Store(pendingMessage, outboxTx, new NServiceBus.Extensibility.ContextBag());

        Assert.DoesNotThrowAsync(() => OutboxCleaner.TryCleanupOnceAsync(
            factory, tablePrefix: "", endpointName: TestEndpoint,
            retentionPeriod: TimeSpan.FromDays(7),
            batchSize: 100, cancellationToken: CancellationToken.None));

        await outboxTx.Commit();

        // After contention clears, cleanup must succeed on the next attempt.
        var deleted = await OutboxCleaner.CleanupOnce(
            factory, tablePrefix: "", endpointName: TestEndpoint,
            retentionPeriod: TimeSpan.FromDays(7),
            batchSize: 100, cancellationToken: CancellationToken.None);

        Assert.That(deleted, Is.EqualTo(1));
        Assert.That(await Exists("in-flight"), Is.True, "the outbox tx must commit cleanly");
    }

    [Test]
    public async Task CleanupOnce_OnlyAffectsConfiguredEndpoint()
    {
        var old = DateTime.UtcNow - TimeSpan.FromDays(30);
        await Insert("shared-id", dispatched: true, dispatchedAt: old, endpointName: TestEndpoint);
        await Insert("shared-id", dispatched: true, dispatchedAt: old, endpointName: "other-endpoint");

        var deleted = await OutboxCleaner.CleanupOnce(
            factory, tablePrefix: "", endpointName: TestEndpoint,
            retentionPeriod: TimeSpan.FromDays(7),
            batchSize: 1000, cancellationToken: CancellationToken.None);

        Assert.That(deleted, Is.EqualTo(1));
        Assert.That(await CountRows(), Is.EqualTo(1), "The other-endpoint record should remain.");
    }

    [Test]
    public void TryCleanupOnceAsync_FactoryThrowsSqliteException_DoesNotPropagate()
    {
        // Driver-level SQLite errors must not kill the cleanup loop; they should be logged and
        // swallowed so the next iteration retries.
        var poisonedFactory = new ThrowingConnectionFactory(
            () => throw new SqliteException("simulated transient failure", 5));

        Assert.DoesNotThrowAsync(() => OutboxCleaner.TryCleanupOnceAsync(
            poisonedFactory, tablePrefix: "", endpointName: TestEndpoint,
            retentionPeriod: TimeSpan.FromDays(7), batchSize: 100,
            cancellationToken: CancellationToken.None));
    }

    [Test]
    public void TryCleanupOnceAsync_FactoryThrowsIOException_DoesNotPropagate()
    {
        var poisonedFactory = new ThrowingConnectionFactory(
            () => throw new IOException("simulated disk-side error"));

        Assert.DoesNotThrowAsync(() => OutboxCleanerTests.RunCleanupOnce(poisonedFactory));
    }

    [Test]
    public void TryCleanupOnceAsync_FactoryThrowsUnexpectedException_Propagates()
    {
        // An unexpected exception type signals a real bug (misconfiguration, contract violation).
        // The cleanup loop should crash loudly rather than swallow it and spin forever.
        var poisonedFactory = new ThrowingConnectionFactory(
            () => throw new InvalidOperationException("simulated bug"));

        Assert.ThrowsAsync<InvalidOperationException>(() => RunCleanupOnce(poisonedFactory));
    }

    static Task RunCleanupOnce(IConnectionFactory poisonedFactory) =>
        OutboxCleaner.TryCleanupOnceAsync(
            poisonedFactory, tablePrefix: "", endpointName: TestEndpoint,
            retentionPeriod: TimeSpan.FromDays(7), batchSize: 100,
            cancellationToken: CancellationToken.None);

    sealed class ThrowingConnectionFactory(Func<SqliteConnection> behaviour) : IConnectionFactory
    {
        public ValueTask<SqliteConnection> OpenConnection(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(behaviour());
    }

    Task Insert(string messageId, bool dispatched, DateTime? dispatchedAt) =>
        Insert(messageId, dispatched, dispatchedAt, TestEndpoint);

    async Task Insert(string messageId, bool dispatched, DateTime? dispatchedAt, string endpointName)
    {
        await using var conn = await factory.OpenConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO OutboxRecord (MessageId, EndpointName, Dispatched, DispatchedAt, OperationsJson, PersistenceVersion)
            VALUES ($id, $ep, $disp, $when, $ops, '1');
            """;
        cmd.Parameters.AddWithValue("$id", messageId);
        cmd.Parameters.AddWithValue("$ep", endpointName);
        cmd.Parameters.AddWithValue("$disp", dispatched ? 1 : 0);
        cmd.Parameters.AddWithValue("$when", (object?)dispatchedAt?.ToString("O") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$ops", dispatched ? (object)DBNull.Value : "[]");
        await cmd.ExecuteNonQueryAsync();
    }

    async Task<bool> Exists(string messageId)
    {
        await using var conn = await factory.OpenConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM OutboxRecord WHERE MessageId = $id;";
        cmd.Parameters.AddWithValue("$id", messageId);
        return (long)(await cmd.ExecuteScalarAsync())! > 0;
    }

    async Task<long> CountRows()
    {
        await using var conn = await factory.OpenConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM OutboxRecord;";
        return (long)(await cmd.ExecuteScalarAsync())!;
    }
}
