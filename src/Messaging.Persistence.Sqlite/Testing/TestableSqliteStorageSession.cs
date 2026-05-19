namespace Messaging.Persistence.Sqlite.Testing;

using Microsoft.Data.Sqlite;
using NServiceBus.Persistence;

/// <summary>
/// A test double for <see cref="ISynchronizedStorageSession"/> backed by a caller-owned
/// <see cref="SqliteConnection"/> and <see cref="SqliteTransaction"/>. Plug this into a
/// testable message-handler context to exercise saga or user handler code without spinning
/// up the full NServiceBus pipeline.
/// </summary>
/// <remarks>
/// The caller owns both the connection and the transaction and is responsible for opening,
/// committing or rolling back, and disposing them.
/// </remarks>
public sealed class TestableSqliteStorageSession(SqliteConnection connection, SqliteTransaction transaction)
    : ISynchronizedStorageSession, ISqliteStorageSession
{
    /// <inheritdoc/>
    public SqliteConnection Connection { get; } = connection ?? throw new ArgumentNullException(nameof(connection));

    /// <inheritdoc/>
    public SqliteTransaction Transaction { get; } = transaction ?? throw new ArgumentNullException(nameof(transaction));

    /// <inheritdoc/>
    public SqliteCommand CreateCommand()
    {
        var command = Connection.CreateCommand();
        command.Transaction = Transaction;
        return command;
    }
}
