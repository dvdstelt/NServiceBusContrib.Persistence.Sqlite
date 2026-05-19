namespace NServiceBusContrib.Persistence.Sqlite.TransactionalSession.AcceptanceTests;

using NServiceBus;
using NServiceBus.Configuration.AdvancedExtensibility;
using NServiceBus.Transport;

public static class ConfigureExtensions
{
    public static TransportDefinition ConfigureTransport(this EndpointConfiguration configuration) =>
        configuration.GetSettings().Get<TransportDefinition>();
}
