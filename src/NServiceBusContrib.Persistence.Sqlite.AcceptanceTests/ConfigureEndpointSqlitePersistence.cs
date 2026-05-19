namespace NServiceBus.AcceptanceTests;

using NServiceBusContrib.Persistence.Sqlite;
using Microsoft.Data.Sqlite;
using NServiceBus.AcceptanceTesting.Support;

class ConfigureEndpointSqlitePersistence : IConfigureEndpointTestExecution
{
    string? dbPath;

    public Task Configure(string endpointName, EndpointConfiguration configuration, RunSettings settings, PublisherMetadata publisherMetadata)
    {
        dbPath = Path.Combine(Path.GetTempPath(), $"nservicebuscontrib-sqlite-acceptance-{endpointName}-{Guid.NewGuid():N}.db");

        configuration.UsePersistence<SqlitePersistence>().ConnectionString($"Data Source={dbPath}");
        configuration.EnableInstallers();

        return Task.CompletedTask;
    }

    public Task Cleanup()
    {
        SqliteConnection.ClearAllPools();
        if (dbPath is not null)
        {
            try { File.Delete(dbPath); }
            catch (IOException) { /* file may briefly remain locked */ }
        }
        return Task.CompletedTask;
    }
}
