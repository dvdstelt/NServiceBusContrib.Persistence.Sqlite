namespace Messaging.Persistence.Sqlite.Sagas;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NServiceBus.Features;
using NServiceBus.Sagas;

sealed class SagaFeature : Feature
{
    public SagaFeature()
    {
        Enable<SynchronizedStorageFeature>();
        DependsOn<Sagas>();
        DependsOn<SynchronizedStorageFeature>();
    }

    protected override void Setup(FeatureConfigurationContext context)
    {
        var tablePrefix = SqliteSettings.ResolveTablePrefix(context.Settings);
        var connectionFactory = SqliteSettings.ResolveConnectionFactory(context.Settings);
        var sagaInfoCache = new SagaInfoCache(tablePrefix);

        context.Services.TryAddSingleton(connectionFactory);
        context.Services.AddSingleton(sagaInfoCache);
        context.Services.AddSingleton<ISagaPersister, SqliteSagaPersister>();

        context.AddInstaller<SagaInstaller>();
    }
}
