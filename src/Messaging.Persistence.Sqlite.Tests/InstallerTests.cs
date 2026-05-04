namespace Messaging.Persistence.Sqlite.Tests;

using Messaging.Persistence.Sqlite.Outbox;
using Messaging.Persistence.Sqlite.Sagas;
using Messaging.Persistence.Sqlite.Subscriptions;
using Microsoft.Data.Sqlite;
using NServiceBus.Settings;
using NUnit.Framework;

[TestFixture]
public class InstallerTests
{
    string dbPath = null!;
    DefaultConnectionFactory factory = null!;

    [SetUp]
    public void SetUp()
    {
        dbPath = Path.Combine(Path.GetTempPath(), $"messaging-sqlite-installer-{Guid.NewGuid():N}.db");
        factory = new DefaultConnectionFactory($"Data Source={dbPath}");
    }

    [TearDown]
    public void TearDown()
    {
        SqliteConnection.ClearAllPools();
        try { File.Delete(dbPath); } catch { /* file may briefly remain locked */ }
    }

    [Test]
    public async Task OutboxInstaller_CreatesOutboxTable()
    {
        var installer = new OutboxInstaller(factory, EmptySettings());
        await installer.Install("test", CancellationToken.None);

        Assert.That(await TableExists("OutboxRecord"), Is.True);
    }

    [Test]
    public async Task OutboxInstaller_RunTwice_NoError()
    {
        var installer = new OutboxInstaller(factory, EmptySettings());
        await installer.Install("test", CancellationToken.None);
        Assert.DoesNotThrowAsync(() => installer.Install("test", CancellationToken.None));
    }

    [Test]
    public async Task OutboxInstaller_AppliesTablePrefix()
    {
        var installer = new OutboxInstaller(factory, SettingsWithPrefix("MyApp_"));
        await installer.Install("test", CancellationToken.None);

        Assert.That(await TableExists("MyApp_OutboxRecord"), Is.True);
        Assert.That(await TableExists("OutboxRecord"), Is.False);
    }

    [Test]
    public async Task SubscriptionInstaller_CreatesSubscriptionTable()
    {
        var installer = new SubscriptionInstaller(factory, EmptySettings());
        await installer.Install("test", CancellationToken.None);

        Assert.That(await TableExists("SubscriptionRecord"), Is.True);
    }

    [Test]
    public async Task SagaInstaller_CreatesTablesForDiscoveredSagas()
    {
        var cache = new SagaInfoCache(tablePrefix: "");
        var installer = new SagaInstaller(factory, cache);
        await installer.Install("test", CancellationToken.None);

        Assert.That(await TableExists(nameof(InstallerTestSagaData)), Is.True);
    }

    static IReadOnlySettings EmptySettings() => new SettingsHolder();

    static IReadOnlySettings SettingsWithPrefix(string prefix)
    {
        var holder = new SettingsHolder();
        holder.Set(SettingsKeys.TablePrefix, prefix);
        return holder;
    }

    async Task<bool> TableExists(string tableName)
    {
        await using var conn = await factory.OpenConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $name;";
        cmd.Parameters.AddWithValue("$name", tableName);
        return (long)(await cmd.ExecuteScalarAsync())! == 1;
    }

    public sealed class InstallerTestSagaData : NServiceBus.ContainSagaData
    {
        public string Marker { get; set; } = "";
    }
}
