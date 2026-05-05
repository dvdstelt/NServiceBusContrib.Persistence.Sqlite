namespace Messaging.Persistence.Sqlite.Subscriptions;

using NServiceBus.Extensibility;
using NServiceBus.Unicast.Subscriptions;
using NServiceBus.Unicast.Subscriptions.MessageDrivenSubscriptions;

sealed class SqliteSubscriptionPersister(IConnectionFactory connectionFactory, string tablePrefix) : ISubscriptionStorage
{
    public const string PersistenceVersion = "1";

    readonly string subscriptionTable = $"{tablePrefix}SubscriptionRecord";

    public async Task Subscribe(Subscriber subscriber, MessageType messageType, ContextBag context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subscriber);
        ArgumentNullException.ThrowIfNull(messageType);

        await using var connection = await connectionFactory.OpenConnection(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        // Preserve any previously-recorded Endpoint when the new caller omits it.
        // INSERT OR REPLACE would erase the column to NULL on every re-subscribe.
        command.CommandText = $"""
            INSERT INTO {subscriptionTable} (MessageType, Subscriber, Endpoint, PersistenceVersion)
            VALUES ($mt, $sub, $ep, $ver)
            ON CONFLICT(MessageType, Subscriber) DO UPDATE SET
                Endpoint = COALESCE(excluded.Endpoint, Endpoint),
                PersistenceVersion = excluded.PersistenceVersion;
            """;
        command.Parameters.AddWithValue("$mt", messageType.TypeName);
        command.Parameters.AddWithValue("$sub", subscriber.TransportAddress);
        command.Parameters.AddWithValue("$ep", (object?)subscriber.Endpoint ?? DBNull.Value);
        command.Parameters.AddWithValue("$ver", PersistenceVersion);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task Unsubscribe(Subscriber subscriber, MessageType messageType, ContextBag context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subscriber);
        ArgumentNullException.ThrowIfNull(messageType);

        await using var connection = await connectionFactory.OpenConnection(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            DELETE FROM {subscriptionTable}
            WHERE MessageType = $mt AND Subscriber = $sub;
            """;
        command.Parameters.AddWithValue("$mt", messageType.TypeName);
        command.Parameters.AddWithValue("$sub", subscriber.TransportAddress);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IEnumerable<Subscriber>> GetSubscriberAddressesForMessage(IEnumerable<MessageType> messageHierarchy, ContextBag context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messageHierarchy);

        var typeNames = messageHierarchy.Select(m => m.TypeName).Distinct().ToArray();
        if (typeNames.Length == 0)
        {
            return [];
        }

        await using var connection = await connectionFactory.OpenConnection(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        var paramNames = new string[typeNames.Length];
        for (var i = 0; i < typeNames.Length; i++)
        {
            paramNames[i] = $"$mt{i}";
            command.Parameters.AddWithValue(paramNames[i], typeNames[i]);
        }

        command.CommandText = $"""
            SELECT DISTINCT Subscriber, Endpoint FROM {subscriptionTable}
            WHERE MessageType IN ({string.Join(", ", paramNames)});
            """;

        var results = new List<Subscriber>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var subscriberAddress = reader.GetString(0);
            var endpoint = reader.IsDBNull(1) ? null : reader.GetString(1);
            results.Add(new Subscriber(subscriberAddress, endpoint));
        }
        return results;
    }
}
