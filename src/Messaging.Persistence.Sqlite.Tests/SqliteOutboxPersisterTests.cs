namespace Messaging.Persistence.Sqlite.Tests;

using Messaging.Persistence.Sqlite.Outbox;
using Microsoft.Data.Sqlite;
using NServiceBus.Extensibility;
using NServiceBus.Outbox;
using NUnit.Framework;
using DispatchProperties = NServiceBus.Transport.DispatchProperties;

[TestFixture]
public class SqliteOutboxPersisterTests
{
    string dbPath = null!;
    DefaultConnectionFactory factory = null!;
    SqliteOutboxPersister persister = null!;

    [SetUp]
    public async Task SetUp()
    {
        dbPath = Path.Combine(Path.GetTempPath(), $"messaging-sqlite-outbox-{Guid.NewGuid():N}.db");
        factory = new DefaultConnectionFactory($"Data Source={dbPath}");

        await using var conn = await factory.OpenConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = SchemaScripts.CreateOutboxTable(tablePrefix: "");
        await cmd.ExecuteNonQueryAsync();

        persister = new SqliteOutboxPersister(factory, tablePrefix: "", endpointName: "test-endpoint");
    }

    [TearDown]
    public void TearDown()
    {
        SqliteConnection.ClearAllPools();
        try { File.Delete(dbPath); } catch { /* file may briefly remain locked */ }
    }

    [Test]
    public async Task Get_NonExisting_ReturnsNull()
    {
        var result = await persister.Get("missing", new ContextBag());
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task Store_RoundTrips_Operations()
    {
        var operations = new[]
        {
            NewOperation("msg-1", body: "hello"),
            NewOperation("msg-2", body: "world")
        };
        var message = new OutboxMessage("incoming-1", operations);

        await using (var tx = await persister.BeginTransaction(new ContextBag()))
        {
            await persister.Store(message, tx, new ContextBag());
            await tx.Commit();
        }

        var fetched = await persister.Get("incoming-1", new ContextBag());

        Assert.That(fetched, Is.Not.Null);
        Assert.That(fetched.MessageId, Is.EqualTo("incoming-1"));
        Assert.That(fetched.TransportOperations.Length, Is.EqualTo(2));
        Assert.That(fetched.TransportOperations[0].MessageId, Is.EqualTo("msg-1"));
        Assert.That(System.Text.Encoding.UTF8.GetString(fetched.TransportOperations[0].Body.Span), Is.EqualTo("hello"));
    }

    [Test]
    public async Task Store_RolledBack_LeavesNoRecord()
    {
        var message = new OutboxMessage("incoming-2", [NewOperation("msg", "x")]);

        await using (var tx = await persister.BeginTransaction(new ContextBag()))
        {
            await persister.Store(message, tx, new ContextBag());
            // No tx.Commit, dispose rolls back.
        }

        var fetched = await persister.Get("incoming-2", new ContextBag());
        Assert.That(fetched, Is.Null);
    }

    [Test]
    public async Task Store_DuplicateMessageId_Throws()
    {
        var message = new OutboxMessage("dup", [NewOperation("op", "first")]);

        await using (var tx = await persister.BeginTransaction(new ContextBag()))
        {
            await persister.Store(message, tx, new ContextBag());
            await tx.Commit();
        }

        await using var tx2 = await persister.BeginTransaction(new ContextBag());
        Assert.ThrowsAsync<InvalidOperationException>(() =>
            persister.Store(message, tx2, new ContextBag()));
    }

    [Test]
    public async Task Store_WrongTransactionType_Throws()
    {
        var message = new OutboxMessage("x", [NewOperation("op", "x")]);
        var alien = new AlienOutboxTransaction();

        Assert.ThrowsAsync<ArgumentException>(() =>
            persister.Store(message, alien, new ContextBag()));
    }

    [Test]
    public async Task SetAsDispatched_ClearsOperations_AndSetsFlag()
    {
        var message = new OutboxMessage("incoming-3", [NewOperation("op", "payload")]);
        await using (var tx = await persister.BeginTransaction(new ContextBag()))
        {
            await persister.Store(message, tx, new ContextBag());
            await tx.Commit();
        }

        await persister.SetAsDispatched("incoming-3", new ContextBag());

        var fetched = await persister.Get("incoming-3", new ContextBag());
        Assert.That(fetched, Is.Not.Null);
        Assert.That(fetched.MessageId, Is.EqualTo("incoming-3"));
        Assert.That(fetched.TransportOperations, Is.Empty);
    }

    [Test]
    public async Task Get_PreservesHeadersAndOptions()
    {
        var op = new TransportOperation(
            "msg-id",
            new DispatchProperties(new Dictionary<string, string> { ["DeliveryConstraint"] = "NonDurable" }),
            System.Text.Encoding.UTF8.GetBytes("body"),
            new Dictionary<string, string> { ["NServiceBus.MessageId"] = "abc", ["Custom"] = "value" });

        var message = new OutboxMessage("hdr-test", [op]);

        await using (var tx = await persister.BeginTransaction(new ContextBag()))
        {
            await persister.Store(message, tx, new ContextBag());
            await tx.Commit();
        }

        var fetched = await persister.Get("hdr-test", new ContextBag());
        var roundTripped = fetched.TransportOperations[0];

        Assert.That(roundTripped.Headers["NServiceBus.MessageId"], Is.EqualTo("abc"));
        Assert.That(roundTripped.Headers["Custom"], Is.EqualTo("value"));
        Assert.That(roundTripped.Options["DeliveryConstraint"], Is.EqualTo("NonDurable"));
    }

    static TransportOperation NewOperation(string messageId, string body) =>
        new(messageId, new DispatchProperties(), System.Text.Encoding.UTF8.GetBytes(body), new Dictionary<string, string>());

    sealed class AlienOutboxTransaction : IOutboxTransaction
    {
        public Task Commit(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
