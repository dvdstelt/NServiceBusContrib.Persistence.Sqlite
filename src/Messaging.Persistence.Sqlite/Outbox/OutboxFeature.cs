namespace Messaging.Persistence.Sqlite.Outbox;

using Microsoft.Extensions.DependencyInjection;
using NServiceBus.Features;
using NServiceBus.Installation;
using NServiceBus.Outbox;

sealed class OutboxFeature : Feature
{
    public OutboxFeature()
    {
        DependsOn<NServiceBus.Features.Outbox>();
        DependsOn<SynchronizedStorageFeature>();
    }

    protected override void Setup(FeatureConfigurationContext context)
    {
        var tablePrefix = SqliteSettings.ResolveTablePrefix(context.Settings);
        var retention = SqliteSettings.ResolveOutboxRetention(context.Settings);
        var cleanupFrequency = SqliteSettings.ResolveOutboxCleanupFrequency(context.Settings);

        context.Services.AddSingleton<IOutboxStorage>(sp =>
            new SqliteOutboxPersister(sp.GetRequiredService<IConnectionFactory>(), tablePrefix));

        context.Services.AddSingleton<INeedToInstallSomething>(sp =>
            new OutboxInstaller(sp.GetRequiredService<IConnectionFactory>(), tablePrefix));

        context.RegisterStartupTask(sp => new OutboxCleaner(
            sp.GetRequiredService<IConnectionFactory>(),
            tablePrefix,
            retention,
            cleanupFrequency));
    }
}
