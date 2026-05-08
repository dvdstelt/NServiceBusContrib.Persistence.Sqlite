namespace Demo.TxClient;

using NServiceBus;
using NServiceBus.Features;

sealed class CaptureProviderFeature : Feature
{
    protected override void Setup(FeatureConfigurationContext context) =>
        context.RegisterStartupTask(sp => new CaptureProviderStartupTask(sp));
}

sealed class CaptureProviderStartupTask(IServiceProvider serviceProvider) : FeatureStartupTask
{
    protected override Task OnStart(IMessageSession session, CancellationToken cancellationToken = default)
    {
        ServiceProviderHolder.Set(serviceProvider);
        return Task.CompletedTask;
    }

    protected override Task OnStop(IMessageSession session, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}

static class ServiceProviderHolder
{
    static readonly TaskCompletionSource<IServiceProvider> ready =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public static Task<IServiceProvider> WaitAsync() => ready.Task;

    public static void Set(IServiceProvider serviceProvider) => ready.TrySetResult(serviceProvider);
}
