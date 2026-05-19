namespace NServiceBusContrib.Persistence.Sqlite;

using Microsoft.Data.Sqlite;

interface IConnectionFactory
{
    ValueTask<SqliteConnection> OpenConnection(CancellationToken cancellationToken = default);
}
