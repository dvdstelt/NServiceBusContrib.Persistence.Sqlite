namespace Messaging.Persistence.Sqlite.Tests;

using NUnit.Framework;

[TestFixture]
public class TablePrefixTests
{
    [TestCase("")]
    [TestCase("MyApp_")]
    [TestCase("ABC123")]
    [TestCase("123foo")]
    [TestCase("_underscore_first")]
    public void Create_AcceptsValidPrefix(string value)
    {
        var prefix = TablePrefix.Create(value);
        Assert.That((string)prefix, Is.EqualTo(value));
    }

    [TestCase("bad-hyphen")]
    [TestCase("bad space")]
    [TestCase("'; DROP TABLE x;--")]
    [TestCase("emoji_\U0001F4A5")]
    public void Create_RejectsInvalidPrefix(string value) =>
        Assert.Throws<ArgumentException>(() => TablePrefix.Create(value));

    [Test]
    public void Empty_ProducesEmptyString() =>
        Assert.That((string)TablePrefix.Empty, Is.EqualTo(""));

    [Test]
    public void ImplicitConversion_AllowsStringInterpolation()
    {
        var prefix = TablePrefix.Create("Foo_");
        var sql = $"SELECT * FROM {prefix}Table";
        Assert.That(sql, Is.EqualTo("SELECT * FROM Foo_Table"));
    }
}
