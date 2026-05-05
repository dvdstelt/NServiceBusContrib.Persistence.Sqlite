namespace Messaging.Persistence.Sqlite;

using Microsoft.Data.Sqlite;

static class SqliteErrors
{
    // SqliteErrorCode == 19 is the primary SQLITE_CONSTRAINT code, which fires for ANY constraint
    // violation (NOT NULL, CHECK, FOREIGN KEY, UNIQUE, PRIMARY KEY). The extended code distinguishes
    // them. Treating all CONSTRAINT failures as "duplicate" hides real schema bugs.
    const int SqliteConstraintPrimaryKey = 1555;
    const int SqliteConstraintUnique = 2067;

    public static bool IsDuplicateKey(SqliteException exception) =>
        exception.SqliteExtendedErrorCode is SqliteConstraintPrimaryKey or SqliteConstraintUnique;
}
