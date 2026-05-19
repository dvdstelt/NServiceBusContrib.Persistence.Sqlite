namespace NServiceBusContrib.Persistence.Sqlite.Tests;

using NServiceBusContrib.Persistence.Sqlite.Sagas;
using Microsoft.Data.Sqlite;
using NServiceBus;
using NServiceBus.Extensibility;
using NServiceBus.Sagas;
using NUnit.Framework;

[TestFixture]
public class SqliteSagaPersisterTests
{
    string dbPath = null!;
    DefaultConnectionFactory factory = null!;
    SqliteSagaPersister persister = null!;
    SqliteSynchronizedStorageSession session = null!;

    [SetUp]
    public async Task SetUp()
    {
        dbPath = Path.Combine(Path.GetTempPath(), $"messaging-sqlite-saga-{Guid.NewGuid():N}.db");
        factory = new DefaultConnectionFactory($"Data Source={dbPath}");

        await using (var conn = await factory.OpenConnection())
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = SchemaScripts.CreateSagaTable("OrderSagaData");
            await cmd.ExecuteNonQueryAsync();
        }

        persister = new SqliteSagaPersister(new SagaInfoCache(tablePrefix: TablePrefix.Empty));
        session = new SqliteSynchronizedStorageSession(factory);
        await session.Open(new ContextBag());
    }

    [TearDown]
    public async Task TearDown()
    {
        await session.DisposeAsync();
        SqliteConnection.ClearAllPools();
        try { File.Delete(dbPath); } catch { /* file may briefly remain locked */ }
    }

    [Test]
    public async Task Save_ThenGetById_RoundTripsData()
    {
        var data = new OrderSagaData { Id = Guid.NewGuid(), OrderId = "order-1", Quantity = 7 };
        var ctx = new ContextBag();

        await persister.Save(data, new SagaCorrelationProperty("OrderId", "order-1"), session, ctx);
        await session.CompleteAsync();
        await session.DisposeAsync();

        session = new SqliteSynchronizedStorageSession(factory);
        await session.Open(new ContextBag());

        var fetched = await persister.Get<OrderSagaData>(data.Id, session, new ContextBag());
        Assert.That(fetched, Is.Not.Null);
        Assert.That(fetched.OrderId, Is.EqualTo("order-1"));
        Assert.That(fetched.Quantity, Is.EqualTo(7));
    }

    [Test]
    public async Task Get_ByCorrelation_FindsExisting()
    {
        var data = new OrderSagaData { Id = Guid.NewGuid(), OrderId = "abc-123", Quantity = 1 };

        await persister.Save(data, new SagaCorrelationProperty("OrderId", "abc-123"), session, new ContextBag());
        await session.CompleteAsync();
        await session.DisposeAsync();

        session = new SqliteSynchronizedStorageSession(factory);
        await session.Open(new ContextBag());

        var fetched = await persister.Get<OrderSagaData>("OrderId", "abc-123", session, new ContextBag());
        Assert.That(fetched, Is.Not.Null);
        Assert.That(fetched.Id, Is.EqualTo(data.Id));
    }

    [Test]
    public async Task Get_NonExistent_ReturnsNull()
    {
        var fetched = await persister.Get<OrderSagaData>(Guid.NewGuid(), session, new ContextBag());
        Assert.That(fetched, Is.Null);
    }

    [Test]
    public async Task Update_RoundTripsAndIncrementsConcurrency()
    {
        var data = new OrderSagaData { Id = Guid.NewGuid(), OrderId = "ord", Quantity = 1 };
        await persister.Save(data, new SagaCorrelationProperty("OrderId", "ord"), session, new ContextBag());
        await session.CompleteAsync();
        await session.DisposeAsync();

        session = new SqliteSynchronizedStorageSession(factory);
        await session.Open(new ContextBag());

        var ctx = new ContextBag();
        var fetched = await persister.Get<OrderSagaData>(data.Id, session, ctx);
        fetched!.Quantity = 99;
        await persister.Update(fetched, session, ctx);
        await session.CompleteAsync();
        await session.DisposeAsync();

        session = new SqliteSynchronizedStorageSession(factory);
        await session.Open(new ContextBag());

        var refetched = await persister.Get<OrderSagaData>(data.Id, session, new ContextBag());
        Assert.That(refetched!.Quantity, Is.EqualTo(99));
    }

    [Test]
    public async Task Update_WithStaleConcurrency_Throws()
    {
        var data = new OrderSagaData { Id = Guid.NewGuid(), OrderId = "stale", Quantity = 1 };
        await persister.Save(data, new SagaCorrelationProperty("OrderId", "stale"), session, new ContextBag());
        await session.CompleteAsync();
        await session.DisposeAsync();

        // Reader A captures version 1.
        session = new SqliteSynchronizedStorageSession(factory);
        await session.Open(new ContextBag());
        var ctxA = new ContextBag();
        var copyA = await persister.Get<OrderSagaData>(data.Id, session, ctxA);
        await session.CompleteAsync();
        await session.DisposeAsync();

        // Reader B captures version 1, updates to version 2.
        session = new SqliteSynchronizedStorageSession(factory);
        await session.Open(new ContextBag());
        var ctxB = new ContextBag();
        var copyB = await persister.Get<OrderSagaData>(data.Id, session, ctxB);
        copyB!.Quantity = 50;
        await persister.Update(copyB, session, ctxB);
        await session.CompleteAsync();
        await session.DisposeAsync();

        // Reader A's update should fail because the row has moved to version 2.
        session = new SqliteSynchronizedStorageSession(factory);
        await session.Open(new ContextBag());
        copyA!.Quantity = 1000;
        Assert.ThrowsAsync<InvalidOperationException>(() => persister.Update(copyA, session, ctxA));
    }

    [Test]
    public async Task Complete_RemovesRow()
    {
        var data = new OrderSagaData { Id = Guid.NewGuid(), OrderId = "done", Quantity = 1 };
        await persister.Save(data, new SagaCorrelationProperty("OrderId", "done"), session, new ContextBag());
        await session.CompleteAsync();
        await session.DisposeAsync();

        session = new SqliteSynchronizedStorageSession(factory);
        await session.Open(new ContextBag());
        var ctx = new ContextBag();
        var fetched = await persister.Get<OrderSagaData>(data.Id, session, ctx);
        await persister.Complete(fetched!, session, ctx);
        await session.CompleteAsync();
        await session.DisposeAsync();

        session = new SqliteSynchronizedStorageSession(factory);
        await session.Open(new ContextBag());
        var afterComplete = await persister.Get<OrderSagaData>(data.Id, session, new ContextBag());
        Assert.That(afterComplete, Is.Null);
    }

    [Test]
    public async Task Save_DuplicateCorrelation_Throws()
    {
        var first = new OrderSagaData { Id = Guid.NewGuid(), OrderId = "dup", Quantity = 1 };
        var second = new OrderSagaData { Id = Guid.NewGuid(), OrderId = "dup", Quantity = 2 };

        await persister.Save(first, new SagaCorrelationProperty("OrderId", "dup"), session, new ContextBag());

        Assert.ThrowsAsync<InvalidOperationException>(() =>
            persister.Save(second, new SagaCorrelationProperty("OrderId", "dup"), session, new ContextBag()));
    }

    [Test]
    public void Update_WithoutPriorGet_Throws()
    {
        var data = new OrderSagaData { Id = Guid.NewGuid(), OrderId = "x", Quantity = 1 };

        Assert.ThrowsAsync<InvalidOperationException>(() =>
            persister.Update(data, session, new ContextBag()));
    }

    [Test]
    public async Task TwoSagasOfSameType_InOneContext_TrackConcurrencyIndependently()
    {
        // Concurrency must be stashed per saga Id, not per type. Otherwise loading saga A then
        // saga B (both OrderSagaData) overwrites A's snapshot, and updating A would pick up B's
        // version.
        var a = new OrderSagaData { Id = Guid.NewGuid(), OrderId = "alpha", Quantity = 1 };
        var b = new OrderSagaData { Id = Guid.NewGuid(), OrderId = "beta", Quantity = 2 };

        await persister.Save(a, new SagaCorrelationProperty("OrderId", "alpha"), session, new ContextBag());
        await persister.Save(b, new SagaCorrelationProperty("OrderId", "beta"), session, new ContextBag());
        await session.CompleteAsync();
        await session.DisposeAsync();

        // Bump saga B to version 2 in its own session so the two sagas hold different versions.
        session = new SqliteSynchronizedStorageSession(factory);
        await session.Open(new ContextBag());
        var bumpCtx = new ContextBag();
        var fetchedB = await persister.Get<OrderSagaData>(b.Id, session, bumpCtx);
        fetchedB!.Quantity = 200;
        await persister.Update(fetchedB, session, bumpCtx);
        await session.CompleteAsync();
        await session.DisposeAsync();

        // Now load both sagas with one ContextBag and update both. The per-Id stash must keep
        // A's version (1) separate from B's version (2).
        session = new SqliteSynchronizedStorageSession(factory);
        await session.Open(new ContextBag());
        var sharedCtx = new ContextBag();
        var loadedA = await persister.Get<OrderSagaData>(a.Id, session, sharedCtx);
        var loadedB = await persister.Get<OrderSagaData>(b.Id, session, sharedCtx);
        loadedA!.Quantity = 11;
        loadedB!.Quantity = 22;
        await persister.Update(loadedA, session, sharedCtx);
        await persister.Update(loadedB, session, sharedCtx);
        await session.CompleteAsync();
        await session.DisposeAsync();

        session = new SqliteSynchronizedStorageSession(factory);
        await session.Open(new ContextBag());
        var finalA = await persister.Get<OrderSagaData>(a.Id, session, new ContextBag());
        var finalB = await persister.Get<OrderSagaData>(b.Id, session, new ContextBag());
        Assert.That(finalA!.Quantity, Is.EqualTo(11));
        Assert.That(finalB!.Quantity, Is.EqualTo(22));
    }

    [Test]
    public async Task Update_TwoConcurrentSessions_ExactlyOneSucceeds()
    {
        // Existing concurrency tests run the two sessions sequentially. This one uses a
        // barrier so both sessions Get the saga (capturing the same Concurrency=1 snapshot)
        // BEFORE either issues its Update. Optimistic concurrency must detect the conflict and
        // produce exactly one winner.
        var initial = new OrderSagaData { Id = Guid.NewGuid(), OrderId = "race", Quantity = 0 };
        await persister.Save(initial, new SagaCorrelationProperty("OrderId", "race"), session, new ContextBag());
        await session.CompleteAsync();
        await session.DisposeAsync();

        var bothFetched = new TaskCompletionSource();
        var fetchedCount = 0;

        async Task<bool> AttemptUpdate(int newQuantity)
        {
            var localSession = new SqliteSynchronizedStorageSession(factory);
            var ctx = new ContextBag();
            try
            {
                await localSession.Open(ctx);
                var fetched = await persister.Get<OrderSagaData>(initial.Id, localSession, ctx);
                if (fetched is null)
                {
                    return false;
                }

                if (Interlocked.Increment(ref fetchedCount) == 2)
                {
                    bothFetched.SetResult();
                }
                await bothFetched.Task;

                fetched.Quantity = newQuantity;
                await persister.Update(fetched, localSession, ctx);
                await localSession.CompleteAsync();
                return true;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
            catch (Microsoft.Data.Sqlite.SqliteException)
            {
                // SQLite may surface SQLITE_BUSY_SNAPSHOT on the loser before the optimistic
                // check fires; either failure mode is acceptable. The contract is "at most one winner".
                return false;
            }
            finally
            {
                await localSession.DisposeAsync();
            }
        }

        var taskA = Task.Run(() => AttemptUpdate(11));
        var taskB = Task.Run(() => AttemptUpdate(22));
        var results = await Task.WhenAll(taskA, taskB);

        Assert.That(results.Count(r => r), Is.EqualTo(1),
            "exactly one of the two concurrent updaters must succeed");

        session = new SqliteSynchronizedStorageSession(factory);
        await session.Open(new ContextBag());
        var final = await persister.Get<OrderSagaData>(initial.Id, session, new ContextBag());
        Assert.That(final!.Quantity, Is.AnyOf(11, 22));
    }

    public sealed class OrderSagaData : ContainSagaData
    {
        public string OrderId { get; set; } = "";
        public int Quantity { get; set; }
    }
}
