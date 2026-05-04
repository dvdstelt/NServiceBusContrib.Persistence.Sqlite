namespace Messaging.Persistence.Sqlite;

static class SchemaScripts
{
    public static string CreateSagaTable(string tableName) => $"""
        CREATE TABLE IF NOT EXISTS {tableName} (
            Id                 TEXT    NOT NULL PRIMARY KEY,
            DataJson           TEXT    NOT NULL,
            CorrelationId      TEXT    NULL,
            Concurrency        INTEGER NOT NULL DEFAULT 1,
            PersistenceVersion TEXT    NOT NULL
        ) WITHOUT ROWID;

        CREATE UNIQUE INDEX IF NOT EXISTS UX_{tableName}_CorrelationId
            ON {tableName} (CorrelationId) WHERE CorrelationId IS NOT NULL;
        """;

    public static string CreateSubscriptionTable(string tablePrefix) => $"""
        CREATE TABLE IF NOT EXISTS {tablePrefix}SubscriptionRecord (
            MessageType         TEXT NOT NULL,
            Subscriber          TEXT NOT NULL,
            Endpoint            TEXT NULL,
            PersistenceVersion  TEXT NOT NULL,
            PRIMARY KEY (MessageType, Subscriber)
        ) WITHOUT ROWID;
        """;

    public static string CreateOutboxTable(string tablePrefix) => $"""
        CREATE TABLE IF NOT EXISTS {tablePrefix}OutboxRecord (
            MessageId           TEXT    NOT NULL,
            EndpointName        TEXT    NOT NULL,
            Dispatched          INTEGER NOT NULL DEFAULT 0,
            DispatchedAt        TEXT    NULL,
            OperationsJson      TEXT    NULL,
            PersistenceVersion  TEXT    NOT NULL,
            PRIMARY KEY (MessageId, EndpointName)
        ) WITHOUT ROWID;

        CREATE INDEX IF NOT EXISTS IX_{tablePrefix}OutboxRecord_DispatchedAt
            ON {tablePrefix}OutboxRecord (EndpointName, DispatchedAt) WHERE Dispatched = 1;
        """;
}
