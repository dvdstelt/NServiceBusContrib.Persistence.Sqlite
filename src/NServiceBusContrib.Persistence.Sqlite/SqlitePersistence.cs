namespace NServiceBusContrib.Persistence.Sqlite;

using NServiceBusContrib.Persistence.Sqlite.Outbox;
using NServiceBusContrib.Persistence.Sqlite.Sagas;
using NServiceBusContrib.Persistence.Sqlite.Subscriptions;
using NServiceBus;
using NServiceBus.Persistence;

/// <summary>
/// Configures NServiceBus to use SQLite persistence.
/// </summary>
public class SqlitePersistence : PersistenceDefinition, IPersistenceDefinitionFactory<SqlitePersistence>
{
    SqlitePersistence()
    {
        Supports<StorageType.Outbox, OutboxFeature>();
        Supports<StorageType.Sagas, SagaFeature>();
        Supports<StorageType.Subscriptions, SubscriptionFeature>();
    }

    static SqlitePersistence IPersistenceDefinitionFactory<SqlitePersistence>.Create() => new();
}
