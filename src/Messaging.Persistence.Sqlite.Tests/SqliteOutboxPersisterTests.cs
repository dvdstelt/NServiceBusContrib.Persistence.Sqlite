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

    [Test]
    public async Task Store_ManyParallelDistinctMessageIds_AllSucceed()
    {
        // BEGIN DEFERRED is the persister's central concurrency claim. Parallel writers with
        // distinct MessageIds must all succeed - only at the actual write does SQLite take the
        // (one-at-a-time) writer lock, and busy_timeout absorbs the wait.
        const int parallel = 16;

        var tasks = Enumerable.Range(0, parallel).Select(async i =>
        {
            var message = new OutboxMessage($"distinct-{i}", [NewOperation($"op-{i}", "x")]);
            await using var tx = await persister.BeginTransaction(new ContextBag());
            await persister.Store(message, tx, new ContextBag());
            await tx.Commit();
        }).ToArray();

        await Task.WhenAll(tasks);

        for (var i = 0; i < parallel; i++)
        {
            var fetched = await persister.Get($"distinct-{i}", new ContextBag());
            Assert.That(fetched, Is.Not.Null, $"distinct-{i} should have been stored");
        }
    }

    [Test]
    public async Task Store_ManyParallelSameMessageId_ExactlyOneSucceeds()
    {
        // The dedup invariant: when several workers race to store the same MessageId, the
        // unique constraint on (MessageId, EndpointName) must guarantee exactly one winner.
        // Losers must surface as InvalidOperationException, not raw SqliteException.
        const int parallel = 8;
        const string contestedId = "contested";

        var winners = 0;
        var failures = new List<Exception>();

        var tasks = Enumerable.Range(0, parallel).Select(async _ =>
        {
            try
            {
                var message = new OutboxMessage(contestedId, [NewOperation("op", "x")]);
                await using var tx = await persister.BeginTransaction(new ContextBag());
                await persister.Store(message, tx, new ContextBag());
                await tx.Commit();
                Interlocked.Increment(ref winners);
            }
            catch (InvalidOperationException ex)
            {
                lock (failures) { failures.Add(ex); }
            }
        }).ToArray();

        await Task.WhenAll(tasks);

        Assert.That(winners, Is.EqualTo(1), "exactly one parallel writer must win the dedup race");
        Assert.That(failures, Has.Count.EqualTo(parallel - 1),
            "every loser must surface as InvalidOperationException, not a raw SqliteException");
    }

    [Test]
    public void SetAsDispatched_OnMissingMessage_IsSilentNoOp()
    {
        // The NSB outbox calls SetAsDispatched after successful transport dispatch. If the row
        // is missing it has either been cleaned up or is partitioned under a different
        // EndpointName. Either way the contract permits a silent no-op; this test locks that
        // behaviour in so a future change to throw on row-count == 0 surfaces as a deliberate
        // decision rather than an accident.
        Assert.DoesNotThrowAsync(() =>
            persister.SetAsDispatched("never-stored", new ContextBag()));
    }

    [Test]
    public async Task Store_NonUniqueConstraintViolation_IsNotTranslatedAsDuplicate()
    {
        // Recreate the outbox table with an extra CHECK constraint so we can exercise a
        // non-unique constraint failure. Without the fix, the persister catches
        // SqliteErrorCode == 19 (the primary SQLITE_CONSTRAINT code) and reports the row as
        // "already exists" - hiding NOT NULL / CHECK / FK violations behind a misleading message.
        await using (var conn = await factory.OpenConnection())
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                DROP TABLE OutboxRecord;
                CREATE TABLE OutboxRecord (
                    MessageId           TEXT    NOT NULL CHECK (MessageId NOT LIKE 'X%'),
                    EndpointName        TEXT    NOT NULL,
                    Dispatched          INTEGER NOT NULL DEFAULT 0,
                    DispatchedAt        TEXT    NULL,
                    OperationsJson      TEXT    NULL,
                    PersistenceVersion  TEXT    NOT NULL,
                    PRIMARY KEY (MessageId, EndpointName)
                ) WITHOUT ROWID;
                """;
            await cmd.ExecuteNonQueryAsync();
        }

        var message = new OutboxMessage("X-violates-check", [NewOperation("op", "x")]);

        await using var tx = await persister.BeginTransaction(new ContextBag());
        var thrown = Assert.CatchAsync(() => persister.Store(message, tx, new ContextBag()));

        Assert.That(thrown, Is.Not.Null);
        Assert.That(thrown!.Message, Does.Not.Contain("already exists"),
            "CHECK violations must not be reported as duplicate-key errors.");
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
