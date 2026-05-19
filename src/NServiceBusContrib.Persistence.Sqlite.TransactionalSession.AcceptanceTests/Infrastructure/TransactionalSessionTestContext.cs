namespace NServiceBusContrib.Persistence.Sqlite.TransactionalSession.AcceptanceTests;

using System.Collections.Concurrent;
using System.Reflection;
using NServiceBus.AcceptanceTesting;

public class TransactionalSessionTestContext : ScenarioContext
{
    readonly ConcurrentDictionary<string, IServiceProvider> serviceProvidersByEndpoint = new();

    public IServiceProvider ServiceProvider
    {
        get
        {
            // ScenarioContext exposes the active endpoint via a static internal property; reflection
            // is the only way to read it from outside the framework.
            var currentEndpointProperty = typeof(ScenarioContext)
                .GetProperty("CurrentEndpoint", BindingFlags.Static | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException(
                    "Could not locate ScenarioContext.CurrentEndpoint - the AcceptanceTesting framework may have changed.");

            if (currentEndpointProperty.GetValue(null) is not string endpointName)
            {
                throw new InvalidOperationException(
                    "ServiceProvider can only be accessed from within a When step on an endpoint.");
            }

            if (!serviceProvidersByEndpoint.TryGetValue(endpointName, out var serviceProvider))
            {
                throw new InvalidOperationException(
                    $"No service provider has been registered for endpoint '{endpointName}'.");
            }

            return serviceProvider;
        }
    }

    public void RegisterServiceProvider(IServiceProvider serviceProvider, string endpointName) =>
        serviceProvidersByEndpoint[endpointName] = serviceProvider;
}
