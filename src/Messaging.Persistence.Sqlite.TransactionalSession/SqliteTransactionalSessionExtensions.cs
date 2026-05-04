namespace Messaging.Persistence.Sqlite.TransactionalSession;

using NServiceBus;
using NServiceBus.Configuration.AdvancedExtensibility;
using NServiceBus.Features;
using NServiceBus.TransactionalSession;

/// <summary>
/// SQLite persistence extensions for <see cref="ITransactionalSession"/> support.
/// </summary>
public static class SqliteTransactionalSessionExtensions
{
    /// <summary>
    /// Enables transactional session support on this endpoint with default options.
    /// </summary>
    public static PersistenceExtensions<SqlitePersistence> EnableTransactionalSession(
        this PersistenceExtensions<SqlitePersistence> persistenceExtensions) =>
        EnableTransactionalSession(persistenceExtensions, new TransactionalSessionOptions());

    /// <summary>
    /// Enables transactional session support on this endpoint using the supplied options.
    /// </summary>
    public static PersistenceExtensions<SqlitePersistence> EnableTransactionalSession(
        this PersistenceExtensions<SqlitePersistence> persistenceExtensions,
        TransactionalSessionOptions transactionalSessionOptions)
    {
        ArgumentNullException.ThrowIfNull(persistenceExtensions);
        ArgumentNullException.ThrowIfNull(transactionalSessionOptions);

        var settings = persistenceExtensions.GetSettings();
        settings.Set(transactionalSessionOptions);
        settings.EnableFeature<SqliteTransactionalSession>();

        return persistenceExtensions;
    }

    /// <summary>
    /// Opens the transactional session with default SQLite options.
    /// </summary>
    public static Task Open(this ITransactionalSession session, CancellationToken cancellationToken = default) =>
        session.Open(new SqliteOpenSessionOptions(), cancellationToken);
}
