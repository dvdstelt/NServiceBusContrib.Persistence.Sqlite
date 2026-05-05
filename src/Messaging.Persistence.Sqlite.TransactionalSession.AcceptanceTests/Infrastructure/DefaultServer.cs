namespace Messaging.Persistence.Sqlite.TransactionalSession.AcceptanceTests;

using Messaging.Persistence.Sqlite;
using NServiceBus;
using NServiceBus.AcceptanceTesting;
using NServiceBus.AcceptanceTesting.Customization;
using NServiceBus.AcceptanceTesting.Support;
using NServiceBus.Configuration.AdvancedExtensibility;
using NUnit.Framework;

public class DefaultServer : IEndpointSetupTemplate
{
    public virtual async Task<EndpointConfiguration> GetConfiguration(
        RunDescriptor runDescriptor,
        EndpointCustomizationConfiguration endpointCustomizations,
        Func<EndpointConfiguration, Task> configurationBuilderCustomization)
    {
        var endpointConfiguration = new EndpointConfiguration(endpointCustomizations.EndpointName);

        endpointConfiguration.EnableInstallers();
        endpointConfiguration.UseSerialization<SystemJsonSerializer>();
        endpointConfiguration.Recoverability()
            .Delayed(delayed => delayed.NumberOfRetries(0))
            .Immediate(immediate => immediate.NumberOfRetries(0));
        endpointConfiguration.SendFailedMessagesTo("error");

        var transportStorageDirectory = Path.Combine(
            Path.GetTempPath(),
            "msg-sqlite-ts-transport",
            TestContext.CurrentContext.Test.ID);
        endpointConfiguration.UseTransport(new AcceptanceTestingTransport
        {
            StorageLocation = transportStorageDirectory
        });

        var persistence = endpointConfiguration.UsePersistence<SqlitePersistence>();
        persistence.ConnectionString(SetupFixture.ConnectionString);

        // Stash the persistence handle so endpoint subclasses can chain extra config
        // (e.g., EnableTransactionalSession) without re-resolving it from EndpointConfiguration.
        endpointConfiguration.GetSettings().Set(persistence);

        if (runDescriptor.ScenarioContext is TransactionalSessionTestContext testContext)
        {
            endpointConfiguration.RegisterStartupTask(sp =>
                new CaptureServiceProviderStartupTask(sp, testContext, endpointCustomizations.EndpointName));
        }

        await configurationBuilderCustomization(endpointConfiguration).ConfigureAwait(false);
        endpointConfiguration.ScanTypesForTest(endpointCustomizations);
        return endpointConfiguration;
    }
}
