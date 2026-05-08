using Demo.Messages;
using Demo.TxClient;
using Messaging.Persistence.Sqlite;
using Messaging.Persistence.Sqlite.TransactionalSession;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using NServiceBus;
using NServiceBus.TransactionalSession;

const string ProcessorEndpointName = "Demo.Sales";

var dataDir = Path.Combine(Path.GetTempPath(), "nservicebuscontrib-sqlite-demo");
Directory.CreateDirectory(dataDir);

// TxClient shares the Sales database so its outbox writes are visible to the
// processor endpoint that actually dispatches the messages.
var connectionString = $"Data Source={Path.Combine(dataDir, "demo-sales.db")}";

await EnsureAuditTable(connectionString);

var endpointConfiguration = new EndpointConfiguration("Demo.TxClient");
endpointConfiguration.UseSerialization<SystemJsonSerializer>();
var transport = endpointConfiguration.UseTransport(new LearningTransport
{
    StorageDirectory = Path.Combine(dataDir, "transport"),
});
transport.RouteToEndpoint(typeof(PlaceOrder), ProcessorEndpointName);

var persistence = endpointConfiguration.UsePersistence<SqlitePersistence>();
persistence.ConnectionString(connectionString);
persistence.EnableTransactionalSession(new TransactionalSessionOptions
{
    ProcessorEndpoint = ProcessorEndpointName,
});

endpointConfiguration.EnableOutbox();
endpointConfiguration.EnableInstallers();
endpointConfiguration.EnableFeature<CaptureProviderFeature>();
endpointConfiguration.SendOnly();

Console.WriteLine("[TxClient] Starting send-only endpoint...");
var endpoint = await Endpoint.Start(endpointConfiguration);
var serviceProvider = await ServiceProviderHolder.WaitAsync();
Console.WriteLine("[TxClient] Ready. Press Enter to atomically write audit row + send PlaceOrder, 'q' + Enter to quit.");

while (true)
{
    var line = Console.ReadLine();
    if (string.Equals(line, "q", StringComparison.OrdinalIgnoreCase))
    {
        break;
    }

    var orderId = Guid.NewGuid().ToString("N")[..8];

    using var scope = serviceProvider.CreateScope();
    using var session = scope.ServiceProvider.GetRequiredService<ITransactionalSession>();
    await session.Open(new SqliteOpenSessionOptions());

    var sqliteSession = session.SynchronizedStorageSession.SqliteSession();
    await using (var auditCommand = sqliteSession.CreateCommand())
    {
        auditCommand.CommandText = "INSERT INTO DemoOrderAudit (OrderId, CreatedAt) VALUES ($id, $ts);";
        auditCommand.Parameters.AddWithValue("$id", orderId);
        auditCommand.Parameters.AddWithValue("$ts", DateTime.UtcNow.ToString("O"));
        await auditCommand.ExecuteNonQueryAsync();
    }

    await session.Send(new PlaceOrder { OrderId = orderId, Amount = 99.0m });
    await session.Commit();

    Console.WriteLine($"[TxClient] Atomically wrote audit row + sent PlaceOrder {orderId}");
}

await endpoint.Stop();

static async Task EnsureAuditTable(string connectionString)
{
    await using var connection = new SqliteConnection(connectionString);
    await connection.OpenAsync();
    await using var command = connection.CreateCommand();
    command.CommandText = """
        CREATE TABLE IF NOT EXISTS DemoOrderAudit (
            OrderId   TEXT NOT NULL PRIMARY KEY,
            CreatedAt TEXT NOT NULL
        );
        """;
    await command.ExecuteNonQueryAsync();
}
