namespace NServiceBusContrib.Persistence.Sqlite;

using Microsoft.Data.Sqlite;

sealed class DelegateConnectionFactory(Func<CancellationToken, ValueTask<SqliteConnection>> factory) : IConnectionFactory
{
    public ValueTask<SqliteConnection> OpenConnection(CancellationToken cancellationToken = default)
        => factory(cancellationToken);
}
