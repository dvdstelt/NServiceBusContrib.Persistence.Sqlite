namespace Messaging.Persistence.Sqlite.Outbox;

using NServiceBus.Features;

sealed class OutboxFeature : Feature
{
    public OutboxFeature() => DependsOn<NServiceBus.Features.Outbox>();

    protected override void Setup(FeatureConfigurationContext context)
    {
        // Outbox storage registration is implemented in a later phase.
    }
}
