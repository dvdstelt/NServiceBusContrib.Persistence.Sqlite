namespace Messaging.Persistence.Sqlite.TransactionalSession.AcceptanceTests;

using Microsoft.Data.Sqlite;
using NUnit.Framework;

[SetUpFixture]
public class SetupFixture
{
    public const string SampleTableName = "SampleDocument";

    public static string DatabasePath { get; private set; } = null!;
    public static string ConnectionString { get; private set; } = null!;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        DatabasePath = Path.Combine(
            Path.GetTempPath(),
            $"messaging-sqlite-ts-acceptance-{Guid.NewGuid():N}.db");
        ConnectionString = $"Data Source={DatabasePath}";

        // The tests verify that user-controlled writes through ISqliteStorageSession commit
        // alongside the transactional session's outgoing messages, so the table they write
        // into has to exist before any endpoint starts.
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE TABLE IF NOT EXISTS {SampleTableName} (Id TEXT NOT NULL PRIMARY KEY);";
        await command.ExecuteNonQueryAsync();
    }

    [OneTimeTearDown]
    public void Cleanup()
    {
        SqliteConnection.ClearAllPools();
        try { File.Delete(DatabasePath); }
        catch (IOException) { /* file may briefly remain locked on Windows */ }
    }
}
