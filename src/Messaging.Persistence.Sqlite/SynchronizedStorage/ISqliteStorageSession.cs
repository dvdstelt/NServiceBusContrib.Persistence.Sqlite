namespace Messaging.Persistence.Sqlite;

using Microsoft.Data.Sqlite;

/// <summary>
/// Exposes the <see cref="SqliteConnection"/> and <see cref="SqliteTransaction"/> managed by the persister
/// so user handlers can enlist their own writes in the same transaction.
/// </summary>
public interface ISqliteStorageSession
{
    /// <summary>
    /// The open SQLite connection used by the current message scope.
    /// </summary>
    SqliteConnection Connection { get; }

    /// <summary>
    /// The active SQLite transaction. User commands must set <see cref="SqliteCommand.Transaction"/> to this value.
    /// </summary>
    SqliteTransaction Transaction { get; }
}
