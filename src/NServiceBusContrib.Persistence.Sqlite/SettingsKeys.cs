namespace NServiceBusContrib.Persistence.Sqlite;

static class SettingsKeys
{
    public const string ConnectionString = "NServiceBusContrib.Sqlite.ConnectionString";
    public const string ConnectionFactory = "NServiceBusContrib.Sqlite.ConnectionFactory";
    public const string TablePrefix = "NServiceBusContrib.Sqlite.TablePrefix";
    public const string OutboxRetentionPeriod = "NServiceBusContrib.Sqlite.Outbox.RetentionPeriod";
    public const string OutboxCleanupFrequency = "NServiceBusContrib.Sqlite.Outbox.CleanupFrequency";
    public const string OutboxEndpointName = "NServiceBusContrib.Sqlite.Outbox.EndpointName";
}
