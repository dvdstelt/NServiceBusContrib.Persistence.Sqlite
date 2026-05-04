namespace Messaging.Persistence.Sqlite.Subscriptions;

using NServiceBus.Installation;

sealed class SubscriptionInstaller(IConnectionFactory connectionFactory, string tablePrefix) : INeedToInstallSomething
{
    public async Task Install(string identity, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenConnection(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = SchemaScripts.CreateSubscriptionTable(tablePrefix);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
