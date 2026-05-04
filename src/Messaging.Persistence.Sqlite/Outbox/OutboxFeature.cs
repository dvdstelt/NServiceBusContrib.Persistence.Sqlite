namespace Messaging.Persistence.Sqlite.Outbox;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NServiceBus.Features;
using NServiceBus.Outbox;

sealed class OutboxFeature : Feature
{
    public OutboxFeature()
    {
        Enable<SynchronizedStorageFeature>();
        DependsOn<NServiceBus.Features.Outbox>();
        DependsOn<SynchronizedStorageFeature>();
    }

    protected override void Setup(FeatureConfigurationContext context)
    {
        var tablePrefix = SqliteSettings.ResolveTablePrefix(context.Settings);
        var retention = SqliteSettings.ResolveOutboxRetention(context.Settings);
        var cleanupFrequency = SqliteSettings.ResolveOutboxCleanupFrequency(context.Settings);
        var connectionFactory = SqliteSettings.ResolveConnectionFactory(context.Settings);
        var endpointName = context.Settings.EndpointName();

        context.Services.TryAddSingleton(connectionFactory);

        context.Services.AddSingleton<IOutboxStorage>(sp =>
            new SqliteOutboxPersister(sp.GetRequiredService<IConnectionFactory>(), tablePrefix, endpointName));

        context.AddInstaller<OutboxInstaller>();

        context.RegisterStartupTask(sp => new OutboxCleaner(
            sp.GetRequiredService<IConnectionFactory>(),
            tablePrefix,
            endpointName,
            retention,
            cleanupFrequency));
    }
}
