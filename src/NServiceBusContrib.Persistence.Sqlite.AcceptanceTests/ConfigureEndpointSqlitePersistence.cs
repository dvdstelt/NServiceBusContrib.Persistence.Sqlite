namespace NServiceBus.AcceptanceTests;

using NServiceBusContrib.Persistence.Sqlite;
using Microsoft.Data.Sqlite;
using NServiceBus.AcceptanceTesting.Support;

class ConfigureEndpointSqlitePersistence : IConfigureEndpointTestExecution
{
    const string TestDbPathKey = "NServiceBusContrib.Persistence.Sqlite.TestDbPath";

    string? dbPath;

    public Task Configure(string endpointName, EndpointConfiguration configuration, RunSettings settings, PublisherMetadata publisherMetadata)
    {
        if (!settings.TryGet<string>(TestDbPathKey, out var sharedPath))
        {
            sharedPath = Path.Combine(Path.GetTempPath(), $"messaging-sqlite-acceptance-{Guid.NewGuid():N}.db");
            settings.Set(TestDbPathKey, sharedPath);
        }
        dbPath = sharedPath;

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
