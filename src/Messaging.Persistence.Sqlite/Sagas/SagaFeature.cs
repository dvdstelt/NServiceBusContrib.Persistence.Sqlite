namespace Messaging.Persistence.Sqlite.Sagas;

using Microsoft.Extensions.DependencyInjection;
using NServiceBus.Features;
using NServiceBus.Sagas;

sealed class SagaFeature : Feature
{
    public SagaFeature()
    {
        DependsOn<Sagas>();
        DependsOn<SynchronizedStorageFeature>();
    }

    protected override void Setup(FeatureConfigurationContext context)
    {
        var tablePrefix = SqliteSettings.ResolveTablePrefix(context.Settings);
        var sagaInfoCache = new SagaInfoCache(tablePrefix);
        context.Services.AddSingleton(sagaInfoCache);
        context.Services.AddSingleton<ISagaPersister, SqliteSagaPersister>();
    }
}
