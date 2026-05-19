namespace NServiceBusContrib.Persistence.Sqlite.TransactionalSession.AcceptanceTests;

using NServiceBus;
using NServiceBus.Features;

sealed class CaptureServiceProviderStartupTask : FeatureStartupTask
{
    public CaptureServiceProviderStartupTask(
        IServiceProvider services,
        TransactionalSessionTestContext testContext,
        string endpointName) =>
        testContext.RegisterServiceProvider(services, endpointName);

    protected override Task OnStart(IMessageSession session, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    protected override Task OnStop(IMessageSession session, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
