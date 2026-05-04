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
