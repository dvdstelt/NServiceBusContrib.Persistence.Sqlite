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
    public async Task SagaInstaller_CreatesTablesOnlyForRegisteredSagas()
    {
        // The installer must build tables for sagas registered in SagaMetadataCollection,
        // not every IContainSagaData type the assembly scanner can find. Otherwise unrelated
        // sagas in referenced libraries get tables they will never use.
        var cache = new SagaInfoCache(tablePrefix: "");
        var metadata = new NServiceBus.Sagas.SagaMetadataCollection();
        metadata.AddRange(NServiceBus.Sagas.SagaMetadata.CreateMany([typeof(InstallerTestSaga)]));

        var installer = new SagaInstaller(factory, cache, metadata);
        await installer.Install("test", CancellationToken.None);

        Assert.That(await TableExists(nameof(InstallerTestSagaData)), Is.True,
            "the registered saga's table must be created");
        Assert.That(await TableExists(nameof(NotRegisteredSagaData)), Is.False,
            "an unregistered saga's table must NOT be created");
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

    public sealed class InstallerTestSaga : NServiceBus.Saga<InstallerTestSagaData>,
        NServiceBus.IAmStartedByMessages<InstallerTestStartMessage>
    {
        protected override void ConfigureHowToFindSaga(NServiceBus.SagaPropertyMapper<InstallerTestSagaData> mapper) =>
            mapper.MapSaga(s => s.Marker).ToMessage<InstallerTestStartMessage>(m => m.Marker);

        public Task Handle(InstallerTestStartMessage message, NServiceBus.IMessageHandlerContext context) =>
            Task.CompletedTask;
    }

    public sealed class InstallerTestStartMessage : NServiceBus.ICommand
    {
        public string Marker { get; set; } = "";
    }

    public sealed class NotRegisteredSagaData : NServiceBus.ContainSagaData
    {
    }
}
