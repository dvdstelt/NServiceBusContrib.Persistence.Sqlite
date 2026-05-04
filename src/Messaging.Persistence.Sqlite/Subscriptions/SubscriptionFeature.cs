namespace Messaging.Persistence.Sqlite.Subscriptions;

using NServiceBus.Features;

sealed class SubscriptionFeature : Feature
{
    public SubscriptionFeature() => DependsOn("NServiceBus.Features.MessageDrivenSubscriptions");

    protected override void Setup(FeatureConfigurationContext context)
    {
        // Subscription storage registration is implemented in a later phase.
    }
}
