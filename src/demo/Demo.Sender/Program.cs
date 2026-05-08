using Demo.Messages;
using Demo.Sender;
using Messaging.Persistence.Sqlite;
using NServiceBus;

var dataDir = Path.Combine(Path.GetTempPath(), "nservicebuscontrib-sqlite-demo");
Directory.CreateDirectory(dataDir);

var endpointConfiguration = new EndpointConfiguration("Demo.Sender");
endpointConfiguration.UseSerialization<SystemJsonSerializer>();
var transport = endpointConfiguration.UseTransport(new LearningTransport
{
    StorageDirectory = Path.Combine(dataDir, "transport"),
});
transport.RouteToEndpoint(typeof(PlaceOrder), "Demo.Sales");

var persistence = endpointConfiguration.UsePersistence<SqlitePersistence>();
persistence.ConnectionString($"Data Source={Path.Combine(dataDir, "demo-sender.db")}");

endpointConfiguration.EnableOutbox();
endpointConfiguration.EnableInstallers();

Console.WriteLine("[Sender] Starting endpoint...");
var endpoint = await Endpoint.Start(endpointConfiguration);
Console.WriteLine("[Sender] Ready. Press Enter to send PlaceOrder, type 'q' + Enter to quit.");

while (true)
{
    var line = Console.ReadLine();
    if (string.Equals(line, "q", StringComparison.OrdinalIgnoreCase))
    {
        break;
    }

    var orderId = Guid.NewGuid().ToString("N")[..8];
    await endpoint.Send(new PlaceOrder { OrderId = orderId, Amount = 42.0m });
    Console.WriteLine($"[Sender] Sent PlaceOrder {orderId}");
}

await endpoint.Stop();
