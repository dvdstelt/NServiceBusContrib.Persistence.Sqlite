namespace NServiceBusContrib.Persistence.Sqlite.Tests;

using NServiceBusContrib.Persistence.Sqlite.Sagas;
using NServiceBus;
using NUnit.Framework;

[TestFixture]
public class SagaInfoCacheTests
{
    [Test]
    public void Get_NonIdentifierTypeName_RejectedWithClearMessage()
    {
        // Generic type names contain a backtick (e.g. "MyType`1"). Concatenating that into
        // unquoted SQL would produce a parse error at install time, far away from the cause.
        // Reject up-front with an actionable message.
        var cache = new SagaInfoCache(tablePrefix: TablePrefix.Empty);

        var ex = Assert.Throws<ArgumentException>(() => cache.Get(typeof(GenericSagaData<int>)));

        Assert.That(ex!.Message, Does.Contain("GenericSagaData").IgnoreCase);
        Assert.That(ex.Message.ToLowerInvariant(), Does.Contain("identifier")
            .Or.Contain("not supported")
            .Or.Contain("generic"));
    }

    [Test]
    public void Get_RegularSagaType_ReturnsExpectedTableName()
    {
        var cache = new SagaInfoCache(tablePrefix: TablePrefix.Create("Foo_"));

        var info = cache.Get(typeof(RegularSagaData));

        Assert.That(info.TableName, Is.EqualTo("Foo_RegularSagaData"));
    }

    public sealed class GenericSagaData<T> : ContainSagaData
    {
        public T? Value { get; set; }
    }

    public sealed class RegularSagaData : ContainSagaData
    {
    }
}
