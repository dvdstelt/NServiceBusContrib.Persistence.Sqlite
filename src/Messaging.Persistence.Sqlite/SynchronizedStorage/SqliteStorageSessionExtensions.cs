namespace Messaging.Persistence.Sqlite;

using NServiceBus.Persistence;

/// <summary>
/// Extension methods for accessing the SQLite storage session from a synchronized storage session.
/// </summary>
public static class SqliteStorageSessionExtensions
{
    /// <summary>
    /// Returns the <see cref="ISqliteStorageSession"/> associated with the current message handler context.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the synchronized storage session was not provided by the SQLite persister.
    /// </exception>
    public static ISqliteStorageSession SqliteSession(this ISynchronizedStorageSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session is ISqliteStorageSession sqliteSession)
        {
            return sqliteSession;
        }
        throw new InvalidOperationException(
            "The synchronized storage session is not provided by the SQLite persister. " +
            "Configure NServiceBus to use UsePersistence<SqlitePersistence>() to enable this accessor.");
    }
}
