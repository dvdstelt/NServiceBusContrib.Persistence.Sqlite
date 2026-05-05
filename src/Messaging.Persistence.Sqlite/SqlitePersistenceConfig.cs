namespace Messaging.Persistence.Sqlite;

using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using NServiceBus;
using NServiceBus.Configuration.AdvancedExtensibility;

/// <summary>
/// Configuration extensions for the SQLite persister.
/// </summary>
public static class SqlitePersistenceConfig
{
    /// <summary>
    /// Sets the SQLite connection string used to open new connections.
    /// </summary>
    public static PersistenceExtensions<SqlitePersistence> ConnectionString(
        this PersistenceExtensions<SqlitePersistence> persistence,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(persistence);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        persistence.GetSettings().Set(SettingsKeys.ConnectionString, connectionString);
        return persistence;
    }

    /// <summary>
    /// Provides a custom factory used to open SQLite connections. Overrides <see cref="ConnectionString"/>.
    /// </summary>
    public static PersistenceExtensions<SqlitePersistence> ConnectionFactory(
        this PersistenceExtensions<SqlitePersistence> persistence,
        Func<CancellationToken, ValueTask<SqliteConnection>> connectionFactory)
    {
        ArgumentNullException.ThrowIfNull(persistence);
        ArgumentNullException.ThrowIfNull(connectionFactory);
        persistence.GetSettings().Set(SettingsKeys.ConnectionFactory, connectionFactory);
        return persistence;
    }

    /// <summary>
    /// Sets a prefix applied to every table created by the persister.
    /// Only ASCII letters, digits, and underscores are allowed.
    /// </summary>
    public static PersistenceExtensions<SqlitePersistence> TablePrefix(
        this PersistenceExtensions<SqlitePersistence> persistence,
        string tablePrefix)
    {
        ArgumentNullException.ThrowIfNull(persistence);
        ArgumentNullException.ThrowIfNull(tablePrefix);
        if (!TablePrefixPattern.IsMatch(tablePrefix))
        {
            throw new ArgumentException(
                "Table prefix must contain only ASCII letters, digits, and underscores.",
                nameof(tablePrefix));
        }
        persistence.GetSettings().Set(SettingsKeys.TablePrefix, tablePrefix);
        return persistence;
    }

    /// <summary>
    /// Returns the outbox-specific configuration surface.
    /// </summary>
    public static OutboxConfiguration Outbox(this PersistenceExtensions<SqlitePersistence> persistence)
    {
        ArgumentNullException.ThrowIfNull(persistence);
        return new OutboxConfiguration(persistence.GetSettings());
    }

    static readonly Regex TablePrefixPattern = new("^[A-Za-z0-9_]*$", RegexOptions.Compiled);
}
