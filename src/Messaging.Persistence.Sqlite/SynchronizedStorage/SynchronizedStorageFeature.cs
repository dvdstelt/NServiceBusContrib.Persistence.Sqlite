namespace Messaging.Persistence.Sqlite;

using Microsoft.Extensions.DependencyInjection;
using NServiceBus.Features;
using NServiceBus.Persistence;

sealed class SynchronizedStorageFeature : Feature
{
    public SynchronizedStorageFeature() => DependsOn<SynchronizedStorage>();

    protected override void Setup(FeatureConfigurationContext context)
    {
        var connectionFactory = SqliteSettings.ResolveConnectionFactory(context.Settings);
        context.Services.AddSingleton(connectionFactory);

        context.Services.AddScoped<ICompletableSynchronizedStorageSession>(sp =>
            new SqliteSynchronizedStorageSession(sp.GetRequiredService<IConnectionFactory>()));

        context.Services.AddScoped(sp =>
            (sp.GetRequiredService<ICompletableSynchronizedStorageSession>() as ISqliteStorageSession)!);
    }
}
