namespace Messaging.Persistence.Sqlite.Tests;

using Messaging.Persistence.Sqlite.Subscriptions;
using Microsoft.Data.Sqlite;
using NServiceBus.Extensibility;
using NServiceBus.Unicast.Subscriptions;
using NServiceBus.Unicast.Subscriptions.MessageDrivenSubscriptions;
using NUnit.Framework;

[TestFixture]
public class SqliteSubscriptionPersisterTests
{
    string dbPath = null!;
    DefaultConnectionFactory factory = null!;
    SqliteSubscriptionPersister persister = null!;

    [SetUp]
    public async Task SetUp()
    {
        dbPath = Path.Combine(Path.GetTempPath(), $"messaging-sqlite-sub-{Guid.NewGuid():N}.db");
        factory = new DefaultConnectionFactory($"Data Source={dbPath}");

        await using var conn = await factory.OpenConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = SchemaScripts.CreateSubscriptionTable(tablePrefix: "");
        await cmd.ExecuteNonQueryAsync();

        persister = new SqliteSubscriptionPersister(factory, tablePrefix: "");
    }

    [TearDown]
    public void TearDown()
    {
        SqliteConnection.ClearAllPools();
        try { File.Delete(dbPath); } catch { /* file may briefly remain locked */ }
    }

    [Test]
    public async Task Subscribe_ThenGet_ReturnsSubscriber()
    {
        var subscriber = new Subscriber("queue://A", "EndpointA");
        var messageType = new MessageType("Foo.Bar", new Version(1, 0));

        await persister.Subscribe(subscriber, messageType, new ContextBag());

        var subscribers = (await persister.GetSubscriberAddressesForMessage([messageType], new ContextBag())).ToList();
        Assert.That(subscribers, Has.Count.EqualTo(1));
        Assert.That(subscribers[0].TransportAddress, Is.EqualTo("queue://A"));
        Assert.That(subscribers[0].Endpoint, Is.EqualTo("EndpointA"));
    }

    [Test]
    public async Task Subscribe_Idempotent_NoError()
    {
        var subscriber = new Subscriber("queue://A", "EndpointA");
        var messageType = new MessageType("Foo.Bar", new Version(1, 0));

        await persister.Subscribe(subscriber, messageType, new ContextBag());
        Assert.DoesNotThrowAsync(() => persister.Subscribe(subscriber, messageType, new ContextBag()));

        var subscribers = (await persister.GetSubscriberAddressesForMessage([messageType], new ContextBag())).ToList();
        Assert.That(subscribers, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task Unsubscribe_RemovesSubscriber()
    {
        var subscriber = new Subscriber("queue://A", "EndpointA");
        var messageType = new MessageType("Foo.Bar", new Version(1, 0));

        await persister.Subscribe(subscriber, messageType, new ContextBag());
        await persister.Unsubscribe(subscriber, messageType, new ContextBag());

        var subscribers = (await persister.GetSubscriberAddressesForMessage([messageType], new ContextBag())).ToList();
        Assert.That(subscribers, Is.Empty);
    }

    [Test]
    public async Task GetSubscriberAddressesForMessage_HierarchyLookup()
    {
        var subA = new Subscriber("queue://A", "EndpointA");
        var subB = new Subscriber("queue://B", "EndpointB");
        var baseType = new MessageType("Base.Event", new Version(1, 0));
        var derivedType = new MessageType("Derived.Event", new Version(1, 0));

        await persister.Subscribe(subA, baseType, new ContextBag());
        await persister.Subscribe(subB, derivedType, new ContextBag());

        var subscribers = (await persister.GetSubscriberAddressesForMessage([baseType, derivedType], new ContextBag()))
            .Select(s => s.TransportAddress).ToHashSet();

        Assert.That(subscribers, Is.EquivalentTo(new[] { "queue://A", "queue://B" }));
    }

    [Test]
    public async Task GetSubscriberAddressesForMessage_DistinctSubscribers()
    {
        var subscriber = new Subscriber("queue://A", "EndpointA");
        var typeOne = new MessageType("Foo.One", new Version(1, 0));
        var typeTwo = new MessageType("Foo.Two", new Version(1, 0));

        await persister.Subscribe(subscriber, typeOne, new ContextBag());
        await persister.Subscribe(subscriber, typeTwo, new ContextBag());

        var subscribers = (await persister.GetSubscriberAddressesForMessage([typeOne, typeTwo], new ContextBag())).ToList();
        Assert.That(subscribers, Has.Count.EqualTo(1), "Same transport address should appear once even when subscribed to multiple types.");
    }

    [Test]
    public async Task GetSubscriberAddressesForMessage_EmptyHierarchy_ReturnsEmpty()
    {
        var subscribers = await persister.GetSubscriberAddressesForMessage([], new ContextBag());
        Assert.That(subscribers, Is.Empty);
    }

    [Test]
    public async Task Subscribe_WithoutEndpoint_StoresNull()
    {
        var subscriber = new Subscriber("queue://A", endpoint: null);
        var messageType = new MessageType("Foo.NoEndpoint", new Version(1, 0));

        await persister.Subscribe(subscriber, messageType, new ContextBag());

        var subscribers = (await persister.GetSubscriberAddressesForMessage([messageType], new ContextBag())).ToList();
        Assert.That(subscribers, Has.Count.EqualTo(1));
        Assert.That(subscribers[0].Endpoint, Is.Null);
    }
}
