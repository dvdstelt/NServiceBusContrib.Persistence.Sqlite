namespace Messaging.Persistence.Sqlite.Subscriptions;

using Microsoft.Extensions.DependencyInjection;
using NServiceBus.Features;
using NServiceBus.Installation;
using NServiceBus.Unicast.Subscriptions.MessageDrivenSubscriptions;

sealed class SubscriptionFeature : Feature
{
    public SubscriptionFeature() => DependsOn("NServiceBus.Features.MessageDrivenSubscriptions");

    protected override void Setup(FeatureConfigurationContext context)
    {
        var tablePrefix = SqliteSettings.ResolveTablePrefix(context.Settings);
        context.Services.AddSingleton<ISubscriptionStorage>(sp =>
            new SqliteSubscriptionPersister(sp.GetRequiredService<IConnectionFactory>(), tablePrefix));
        context.Services.AddSingleton<INeedToInstallSomething>(sp =>
            new SubscriptionInstaller(sp.GetRequiredService<IConnectionFactory>(), tablePrefix));
    }
}
