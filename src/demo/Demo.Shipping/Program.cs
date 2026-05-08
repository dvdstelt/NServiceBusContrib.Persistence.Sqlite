using Demo.Shipping;
using Messaging.Persistence.Sqlite;
using NServiceBus;

var dataDir = Path.Combine(Path.GetTempPath(), "nservicebuscontrib-sqlite-demo");
Directory.CreateDirectory(dataDir);

var endpointConfiguration = new EndpointConfiguration("Demo.Shipping");
endpointConfiguration.UseSerialization<SystemJsonSerializer>();
endpointConfiguration.UseTransport(new LearningTransport
{
    StorageDirectory = Path.Combine(dataDir, "transport"),
    TransportTransactionMode = TransportTransactionMode.ReceiveOnly,
});

var persistence = endpointConfiguration.UsePersistence<SqlitePersistence>();
persistence.ConnectionString($"Data Source={Path.Combine(dataDir, "demo-shipping.db")}");

endpointConfiguration.EnableOutbox();
endpointConfiguration.EnableInstallers();

Console.WriteLine("[Shipping] Starting endpoint...");
var endpoint = await Endpoint.Start(endpointConfiguration);
Console.WriteLine("[Shipping] Ready. Press Enter to stop.");
Console.ReadLine();
await endpoint.Stop();
