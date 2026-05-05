namespace Messaging.Persistence.Sqlite.Subscriptions;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NServiceBus.Features;
using NServiceBus.Unicast.Subscriptions.MessageDrivenSubscriptions;

sealed class SubscriptionFeature : Feature
{
    public SubscriptionFeature()
    {
        Enable<SynchronizedStorageFeature>();
        // MessageDrivenSubscriptions is internal in NServiceBus 10.x, so we depend on it by name.
        DependsOn("NServiceBus.Features.MessageDrivenSubscriptions");
        DependsOn<SynchronizedStorageFeature>();
    }

    protected override void Setup(FeatureConfigurationContext context)
    {
        var tablePrefix = SqliteSettings.ResolveTablePrefix(context.Settings);
        var connectionFactory = SqliteSettings.ResolveConnectionFactory(context.Settings);
        context.Services.TryAddSingleton(connectionFactory);

        context.Services.AddSingleton<ISubscriptionStorage>(sp =>
            new SqliteSubscriptionPersister(sp.GetRequiredService<IConnectionFactory>(), tablePrefix));

        context.AddInstaller<SubscriptionInstaller>();
    }
}
