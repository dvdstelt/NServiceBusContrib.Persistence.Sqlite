namespace Messaging.Persistence.Sqlite.Tests;

using Messaging.Persistence.Sqlite.Testing;
using Microsoft.Data.Sqlite;
using NUnit.Framework;

[TestFixture]
public class TestableSqliteStorageSessionTests
{
    [Test]
    public async Task SqliteSession_ReturnsTheTestableSession_AndEnlistsCommands()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        await using (var setup = connection.CreateCommand())
        {
            setup.CommandText = "CREATE TABLE Demo (Id INTEGER NOT NULL PRIMARY KEY);";
            await setup.ExecuteNonQueryAsync();
        }

        await using var transaction = connection.BeginTransaction();
        var sut = new TestableSqliteStorageSession(connection, transaction);

        var session = sut.SqliteSession();
        Assert.That(session, Is.SameAs(sut), "the extension should surface the testable session itself");
        Assert.That(session.Connection, Is.SameAs(connection));
        Assert.That(session.Transaction, Is.SameAs(transaction));

        await using (var insert = session.CreateCommand())
        {
            insert.CommandText = "INSERT INTO Demo (Id) VALUES (1);";
            await insert.ExecuteNonQueryAsync();
        }

        // Roll back to confirm the inserted row is enlisted in the caller-owned transaction.
        await transaction.RollbackAsync();

        await using var verify = connection.CreateCommand();
        verify.CommandText = "SELECT COUNT(*) FROM Demo;";
        var count = (long)(await verify.ExecuteScalarAsync())!;
        Assert.That(count, Is.EqualTo(0));
    }

    [Test]
    public void Constructor_RejectsNullArguments()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");

        Assert.Throws<ArgumentNullException>(() => new TestableSqliteStorageSession(null!, null!));
    }
}
