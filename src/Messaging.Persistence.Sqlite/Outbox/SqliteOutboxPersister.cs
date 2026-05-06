namespace Messaging.Persistence.Sqlite.Outbox;

using System.Data;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using NServiceBus.Extensibility;
using NServiceBus.Outbox;

sealed class SqliteOutboxPersister(IConnectionFactory connectionFactory, TablePrefix tablePrefix, string endpointName) : IOutboxStorage
{
    public const string PersistenceVersion = "1";

    static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    readonly string outboxTable = $"{tablePrefix}OutboxRecord";

    public async Task<OutboxMessage> Get(string messageId, ContextBag context, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(messageId);

        await using var connection = await connectionFactory.OpenConnection(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT Dispatched, OperationsJson FROM {outboxTable} WHERE MessageId = $id AND EndpointName = $ep;";
        command.Parameters.AddWithValue("$id", messageId);
        command.Parameters.AddWithValue("$ep", endpointName);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null!;
        }

        var dispatched = reader.GetInt64(0) != 0;
        var json = reader.IsDBNull(1) ? null : reader.GetString(1);

        if (dispatched || string.IsNullOrEmpty(json))
        {
            return new OutboxMessage(messageId, []);
        }

        var operations = JsonSerializer.Deserialize<StorageTransportOperation[]>(json, SerializerOptions) ?? [];
        return new OutboxMessage(messageId, operations.Select(op => op.ToTransportOperation()).ToArray());
    }

    public async Task<IOutboxTransaction> BeginTransaction(ContextBag context, CancellationToken cancellationToken = default)
    {
        var connection = await connectionFactory.OpenConnection(cancellationToken).ConfigureAwait(false);
        try
        {
            // BEGIN DEFERRED so multiple outbox transactions can coexist; the unique constraint on
            // (MessageId, EndpointName) is what enforces dedup, not SQLite's writer serialization.
            // BeginTransactionAsync(IsolationLevel) in Microsoft.Data.Sqlite maps every isolation
            // level to BEGIN IMMEDIATE, so we use the sync deferred overload instead.
            var transaction = connection.BeginTransaction(deferred: true);
            return new SqliteOutboxTransaction(connection, transaction);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async Task Store(OutboxMessage message, IOutboxTransaction transaction, ContextBag context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(transaction);

        if (transaction is not SqliteOutboxTransaction sqliteTx)
        {
            throw new ArgumentException(
                $"Expected outbox transaction of type {nameof(SqliteOutboxTransaction)}.",
                nameof(transaction));
        }

        var operations = message.TransportOperations
            .Select(StorageTransportOperation.From)
            .ToArray();
        var json = JsonSerializer.Serialize(operations, SerializerOptions);

        await using var command = sqliteTx.Connection.CreateCommand();
        command.Transaction = sqliteTx.Transaction;
        command.CommandText = $"""
            INSERT INTO {outboxTable} (MessageId, EndpointName, Dispatched, OperationsJson, PersistenceVersion)
            VALUES ($id, $ep, 0, $ops, $ver);
            """;
        command.Parameters.AddWithValue("$id", message.MessageId);
        command.Parameters.AddWithValue("$ep", endpointName);
        command.Parameters.AddWithValue("$ops", json);
        command.Parameters.AddWithValue("$ver", PersistenceVersion);

        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (SqliteException ex) when (SqliteErrors.IsDuplicateKey(ex))
        {
            throw new InvalidOperationException(
                $"An outbox record with id '{message.MessageId}' already exists for endpoint '{endpointName}'.", ex);
        }
    }

    public async Task SetAsDispatched(string messageId, ContextBag context, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(messageId);

        await using var connection = await connectionFactory.OpenConnection(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            UPDATE {outboxTable}
            SET Dispatched = 1, DispatchedAt = $when, OperationsJson = NULL
            WHERE MessageId = $id AND EndpointName = $ep;
            """;
        command.Parameters.AddWithValue("$id", messageId);
        command.Parameters.AddWithValue("$ep", endpointName);
        command.Parameters.AddWithValue("$when", DateTime.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
