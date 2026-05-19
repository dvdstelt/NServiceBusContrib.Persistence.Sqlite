namespace NServiceBusContrib.Persistence.Sqlite;

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
    /// The active SQLite transaction. Prefer <see cref="CreateCommand"/> over wiring this up by hand.
    /// </summary>
    SqliteTransaction Transaction { get; }

    /// <summary>
    /// Creates a new <see cref="SqliteCommand"/> already bound to <see cref="Connection"/> and
    /// <see cref="Transaction"/>. Use this for user writes inside a handler so they enlist in the
    /// message-scoped transaction without forgetting the binding.
    /// </summary>
    SqliteCommand CreateCommand();
}
