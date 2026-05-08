namespace Messaging.Persistence.Sqlite.Outbox;

using Microsoft.Extensions.DependencyInjection;
using NServiceBus.Installation;
using NServiceBus.Outbox;
using NServiceBus.Settings;

sealed class OutboxInstaller(IServiceProvider serviceProvider) : INeedToInstallSomething
{
    public async Task Install(string identity, CancellationToken cancellationToken = default)
    {
        // INeedToInstallSomething is discovered by assembly scanning, so this installer can be
        // activated on endpoints that didn't enable the outbox. Bail out cleanly in that case
        // to avoid creating an unused outbox table.
        if (serviceProvider.GetService<IOutboxStorage>() is null)
        {
            return;
        }

        var connectionFactory = serviceProvider.GetRequiredService<IConnectionFactory>();
        var settings = serviceProvider.GetRequiredService<IReadOnlySettings>();
        var tablePrefix = SqliteSettings.ResolveTablePrefix(settings);

        await using var connection = await connectionFactory.OpenConnection(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = SchemaScripts.CreateOutboxTable(tablePrefix);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
