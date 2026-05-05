namespace Messaging.Persistence.Sqlite.TransactionalSession.AcceptanceTests;

using System.Globalization;
using NServiceBus.AcceptanceTesting.Customization;
using NUnit.Framework;

[TestFixture]
public abstract class NServiceBusAcceptanceTest
{
    [SetUp]
    public void SetUpTestNamingConvention() =>
        Conventions.EndpointNamingConvention = type =>
        {
            // Convert e.g. "...When_using_transactional_session+AnEndpoint" into
            // "UsingTransactionalSession.AnEndpoint" so transport storage paths and logs stay readable.
            var fullName = type.FullName!;
            var afterLastNamespaceDot = fullName[(fullName.LastIndexOf('.') + 1)..];
            var parts = afterLastNamespaceDot.Split('+');
            var declaringTestClass = parts[0];
            var endpointBuilder = parts[^1];

            if (declaringTestClass.StartsWith("When_", StringComparison.Ordinal))
            {
                declaringTestClass = declaringTestClass[5..];
            }

            var titled = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(declaringTestClass).Replace("_", "");
            return $"{titled}.{endpointBuilder}";
        };
}
