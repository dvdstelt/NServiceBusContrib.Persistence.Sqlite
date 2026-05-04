namespace Messaging.Persistence.Sqlite.Sagas;

using NServiceBus.Features;

sealed class SagaFeature : Feature
{
    public SagaFeature() => DependsOn<Sagas>();

    protected override void Setup(FeatureConfigurationContext context)
    {
        // Saga storage registration is implemented in a later phase.
    }
}
