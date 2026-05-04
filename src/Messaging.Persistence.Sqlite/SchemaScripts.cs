namespace Messaging.Persistence.Sqlite;

static class SchemaScripts
{
    public static string CreateOutboxTable(string tablePrefix) => $"""
        CREATE TABLE IF NOT EXISTS {tablePrefix}OutboxRecord (
            MessageId           TEXT    NOT NULL PRIMARY KEY,
            Dispatched          INTEGER NOT NULL DEFAULT 0,
            DispatchedAt        TEXT    NULL,
            OperationsJson      TEXT    NULL,
            PersistenceVersion  TEXT    NOT NULL
        ) WITHOUT ROWID;

        CREATE INDEX IF NOT EXISTS IX_{tablePrefix}OutboxRecord_DispatchedAt
            ON {tablePrefix}OutboxRecord (DispatchedAt) WHERE Dispatched = 1;
        """;
}
