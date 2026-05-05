namespace Messaging.Persistence.Sqlite.TransactionalSession.AcceptanceTests;

using Messaging.Persistence.Sqlite;
using Messaging.Persistence.Sqlite.TransactionalSession;
using NServiceBus;
using NServiceBus.AcceptanceTesting.Support;
using NServiceBus.Configuration.AdvancedExtensibility;

public class TransactionSessionDefaultServer : DefaultServer
{
    public override async Task<EndpointConfiguration> GetConfiguration(
        RunDescriptor runDescriptor,
        EndpointCustomizationConfiguration endpointCustomization,
        Func<EndpointConfiguration, Task> configurationBuilderCustomization)
    {
        var endpointConfiguration = await base
            .GetConfiguration(runDescriptor, endpointCustomization, configurationBuilderCustomization)
            .ConfigureAwait(false);

        endpointConfiguration.GetSettings()
            .Get<PersistenceExtensions<SqlitePersistence>>()
            .EnableTransactionalSession();

        return endpointConfiguration;
    }
}
