namespace Messaging.Persistence.Sqlite.Tests;

using NServiceBus;
using NUnit.Framework;

[TestFixture]
public class SqlitePersistenceConfigTests
{
    static PersistenceExtensions<SqlitePersistence> Persistence() =>
        new EndpointConfiguration("test-endpoint").UsePersistence<SqlitePersistence>();

    [Test]
    public void ConnectionString_NullOrWhitespace_Throws()
    {
        var persistence = Persistence();
        Assert.Throws<ArgumentNullException>(() => persistence.ConnectionString(null!));
        Assert.Throws<ArgumentException>(() => persistence.ConnectionString(""));
        Assert.Throws<ArgumentException>(() => persistence.ConnectionString("   "));
    }

    [Test]
    public void ConnectionString_Valid_Accepted()
    {
        var persistence = Persistence();
        Assert.DoesNotThrow(() => persistence.ConnectionString("Data Source=:memory:"));
    }

    [Test]
    public void ConnectionFactory_Null_Throws()
    {
        var persistence = Persistence();
        Assert.Throws<ArgumentNullException>(() => persistence.ConnectionFactory(null!));
    }

    [Test]
    public void TablePrefix_InvalidCharacters_Throws()
    {
        var persistence = Persistence();
        Assert.Throws<ArgumentException>(() => persistence.TablePrefix("bad-prefix"));
        Assert.Throws<ArgumentException>(() => persistence.TablePrefix("bad prefix"));
        Assert.Throws<ArgumentException>(() => persistence.TablePrefix("'; DROP TABLE x;--"));
        Assert.Throws<ArgumentException>(() => persistence.TablePrefix("emoji_\U0001F4A5"));
    }

    [Test]
    public void TablePrefix_Null_Throws()
    {
        var persistence = Persistence();
        Assert.Throws<ArgumentNullException>(() => persistence.TablePrefix(null!));
    }

    [Test]
    public void TablePrefix_ValidPrefix_Accepted()
    {
        var persistence = Persistence();
        Assert.DoesNotThrow(() => persistence.TablePrefix("my_prefix_"));
        Assert.DoesNotThrow(() => persistence.TablePrefix(""));
        Assert.DoesNotThrow(() => persistence.TablePrefix("ABC123"));
    }

    [Test]
    public void OutboxRetention_NonPositive_Throws()
    {
        var outbox = Persistence().Outbox();
        Assert.Throws<ArgumentOutOfRangeException>(() => outbox.KeepDeduplicationDataFor(TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => outbox.KeepDeduplicationDataFor(TimeSpan.FromSeconds(-1)));
    }

    [Test]
    public void OutboxCleanupFrequency_NonPositive_Throws()
    {
        var outbox = Persistence().Outbox();
        Assert.Throws<ArgumentOutOfRangeException>(() => outbox.FrequencyToRunDeduplicationDataCleanup(TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => outbox.FrequencyToRunDeduplicationDataCleanup(TimeSpan.FromSeconds(-1)));
    }

    [Test]
    public void Outbox_Positive_Accepted()
    {
        var outbox = Persistence().Outbox();
        Assert.DoesNotThrow(() => outbox.KeepDeduplicationDataFor(TimeSpan.FromDays(7)));
        Assert.DoesNotThrow(() => outbox.FrequencyToRunDeduplicationDataCleanup(TimeSpan.FromMinutes(1)));
    }
}
