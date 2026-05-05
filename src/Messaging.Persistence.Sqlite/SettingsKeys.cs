namespace Messaging.Persistence.Sqlite;

static class SettingsKeys
{
    public const string ConnectionString = "Messaging.Sqlite.ConnectionString";
    public const string ConnectionFactory = "Messaging.Sqlite.ConnectionFactory";
    public const string TablePrefix = "Messaging.Sqlite.TablePrefix";
    public const string SubscriptionsCacheFor = "Messaging.Sqlite.Subscriptions.CacheFor";
    public const string OutboxRetentionPeriod = "Messaging.Sqlite.Outbox.RetentionPeriod";
    public const string OutboxCleanupFrequency = "Messaging.Sqlite.Outbox.CleanupFrequency";
    public const string OutboxEndpointName = "Messaging.Sqlite.Outbox.EndpointName";
}
