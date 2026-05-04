namespace Messaging.Persistence.Sqlite.Outbox;

using NServiceBus.Installation;
using NServiceBus.Settings;

sealed class OutboxInstaller(IConnectionFactory connectionFactory, IReadOnlySettings settings) : INeedToInstallSomething
{
    public async Task Install(string identity, CancellationToken cancellationToken = default)
    {
        var tablePrefix = SqliteSettings.ResolveTablePrefix(settings);
        await using var connection = await connectionFactory.OpenConnection(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = SchemaScripts.CreateOutboxTable(tablePrefix);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
