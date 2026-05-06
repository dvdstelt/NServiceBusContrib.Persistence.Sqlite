namespace Messaging.Persistence.Sqlite;

using Microsoft.Data.Sqlite;
using NServiceBus;
using NServiceBus.Settings;

static class SqliteSettings
{
    public static readonly TimeSpan DefaultOutboxRetention = TimeSpan.FromDays(7);
    public static readonly TimeSpan DefaultOutboxCleanupFrequency = TimeSpan.FromMinutes(1);

    public static IConnectionFactory ResolveConnectionFactory(IReadOnlySettings settings)
    {
        if (settings.TryGet<Func<CancellationToken, ValueTask<SqliteConnection>>>(SettingsKeys.ConnectionFactory, out var customFactory))
        {
            return new DelegateConnectionFactory(customFactory);
        }
        if (settings.TryGet<string>(SettingsKeys.ConnectionString, out var connectionString))
        {
            return new DefaultConnectionFactory(connectionString);
        }
        throw new InvalidOperationException(
            "SQLite persistence requires either a connection string or a connection factory to be configured. " +
            "Call .ConnectionString(...) or .ConnectionFactory(...) on the persistence configuration.");
    }

    public static TablePrefix ResolveTablePrefix(IReadOnlySettings settings) =>
        settings.TryGet<string>(SettingsKeys.TablePrefix, out var prefix)
            ? TablePrefix.Create(prefix)
            : TablePrefix.Empty;

    public static TimeSpan ResolveOutboxRetention(IReadOnlySettings settings) =>
        settings.TryGet<TimeSpan>(SettingsKeys.OutboxRetentionPeriod, out var d) ? d : DefaultOutboxRetention;

    public static TimeSpan ResolveOutboxCleanupFrequency(IReadOnlySettings settings) =>
        settings.TryGet<TimeSpan>(SettingsKeys.OutboxCleanupFrequency, out var f) ? f : DefaultOutboxCleanupFrequency;

    // The send-only + processor-endpoint TransactionalSession topology overrides this to the
    // ProcessorEndpoint so both endpoints read and write the same outbox rows. Otherwise it
    // falls back to the local endpoint name.
    public static string ResolveOutboxEndpointName(IReadOnlySettings settings) =>
        settings.TryGet<string>(SettingsKeys.OutboxEndpointName, out var name) && !string.IsNullOrEmpty(name)
            ? name
            : settings.EndpointName();
}
