using Demo.Sales;
using Messaging.Persistence.Sqlite;
using NServiceBus;

var dataDir = DemoPaths.EnsureDataDirectory();

var endpointConfiguration = new EndpointConfiguration("Demo.Sales");
endpointConfiguration.UseSerialization<SystemJsonSerializer>();
endpointConfiguration.UseTransport(new LearningTransport
{
    StorageDirectory = DemoPaths.TransportDirectory(dataDir),
    TransportTransactionMode = TransportTransactionMode.ReceiveOnly,
});

var persistence = endpointConfiguration.UsePersistence<SqlitePersistence>();
persistence.ConnectionString(DemoPaths.SalesConnectionString(dataDir));

endpointConfiguration.EnableOutbox();
endpointConfiguration.EnableInstallers();

Console.WriteLine("[Sales] Starting endpoint...");
var endpoint = await Endpoint.Start(endpointConfiguration);
Console.WriteLine("[Sales] Ready. Press Enter to stop.");
Console.ReadLine();
await endpoint.Stop();
