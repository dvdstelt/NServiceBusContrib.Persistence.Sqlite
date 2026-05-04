namespace Messaging.Persistence.Sqlite.Tests;

using Messaging.Persistence.Sqlite.Sagas;
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

        persister = new SqliteSagaPersister(new SagaInfoCache(tablePrefix: ""));
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

    public sealed class OrderSagaData : ContainSagaData
    {
        public string OrderId { get; set; } = "";
        public int Quantity { get; set; }
    }
}
