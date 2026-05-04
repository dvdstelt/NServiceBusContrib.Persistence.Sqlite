namespace Messaging.Persistence.Sqlite;

using Messaging.Persistence.Sqlite.Outbox;
using Messaging.Persistence.Sqlite.Sagas;
using Messaging.Persistence.Sqlite.Subscriptions;
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
