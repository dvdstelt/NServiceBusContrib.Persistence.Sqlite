namespace NServiceBusContrib.Persistence.Sqlite.Subscriptions;

using Microsoft.Extensions.DependencyInjection;
using NServiceBus.Installation;
using NServiceBus.Settings;
using NServiceBus.Unicast.Subscriptions.MessageDrivenSubscriptions;

sealed class SubscriptionInstaller(IServiceProvider serviceProvider) : INeedToInstallSomething
{
    public async Task Install(string identity, CancellationToken cancellationToken = default)
    {
        // INeedToInstallSomething is discovered by assembly scanning, so this installer can be
        // activated on endpoints that don't use message-driven subscriptions. Bail out cleanly
        // in that case to avoid creating an unused subscription table.
        if (serviceProvider.GetService<ISubscriptionStorage>() is null)
        {
            return;
        }

        var connectionFactory = serviceProvider.GetRequiredService<IConnectionFactory>();
        var settings = serviceProvider.GetRequiredService<IReadOnlySettings>();
        var tablePrefix = SqliteSettings.ResolveTablePrefix(settings);

        await using var connection = await connectionFactory.OpenConnection(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = SchemaScripts.CreateSubscriptionTable(tablePrefix);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
