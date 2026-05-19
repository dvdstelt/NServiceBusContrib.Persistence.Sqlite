namespace NServiceBusContrib.Persistence.Sqlite.Sagas;

using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using NServiceBus;
using NServiceBus.Extensibility;
using NServiceBus.Persistence;
using NServiceBus.Sagas;

sealed class SqliteSagaPersister(SagaInfoCache sagaInfoCache) : ISagaPersister
{
    public const string PersistenceVersion = "1";

    static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public async Task Save(IContainSagaData sagaData, SagaCorrelationProperty correlationProperty,
        ISynchronizedStorageSession session, ContextBag context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sagaData);
        var sqliteSession = session.SqliteSession();
        var sagaDataType = sagaData.GetType();
        var info = sagaInfoCache.Get(sagaDataType);

        var dataJson = JsonSerializer.Serialize(sagaData, sagaDataType, SerializerOptions);
        var correlationValue = correlationProperty.Equals(SagaCorrelationProperty.None)
            ? null
            : Convert.ToString(correlationProperty.Value, CultureInfo.InvariantCulture);

        await using var cmd = sqliteSession.CreateCommand();
        cmd.CommandText = $"""
            INSERT INTO {info.TableName} (Id, DataJson, CorrelationId, Concurrency, PersistenceVersion)
            VALUES ($id, $data, $corr, 1, $ver);
            """;
        cmd.Parameters.AddWithValue("$id", sagaData.Id.ToString());
        cmd.Parameters.AddWithValue("$data", dataJson);
        cmd.Parameters.AddWithValue("$corr", (object?)correlationValue ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$ver", PersistenceVersion);

        try
        {
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (SqliteException ex) when (SqliteErrors.IsDuplicateKey(ex))
        {
            throw new InvalidOperationException(
                $"A saga of type '{sagaDataType.Name}' with id '{sagaData.Id}' or correlation '{correlationValue}' already exists.",
                ex);
        }

        StashConcurrency(context, sagaData.Id, 1);
    }

    public async Task Update(IContainSagaData sagaData, ISynchronizedStorageSession session, ContextBag context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sagaData);
        var sqliteSession = session.SqliteSession();
        var sagaDataType = sagaData.GetType();
        var info = sagaInfoCache.Get(sagaDataType);
        var oldConcurrency = RetrieveConcurrency(context, sagaData.Id, sagaDataType);

        var dataJson = JsonSerializer.Serialize(sagaData, sagaDataType, SerializerOptions);

        await using var cmd = sqliteSession.CreateCommand();
        cmd.CommandText = $"""
            UPDATE {info.TableName}
            SET DataJson = $data, Concurrency = Concurrency + 1
            WHERE Id = $id AND Concurrency = $old;
            """;
        cmd.Parameters.AddWithValue("$id", sagaData.Id.ToString());
        cmd.Parameters.AddWithValue("$data", dataJson);
        cmd.Parameters.AddWithValue("$old", oldConcurrency);

        var updated = await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        if (updated != 1)
        {
            throw new InvalidOperationException(
                $"The saga of type '{sagaDataType.Name}' with id '{sagaData.Id}' was updated by another process or no longer exists.");
        }

        StashConcurrency(context, sagaData.Id, oldConcurrency + 1);
    }

    public Task<TSagaData> Get<TSagaData>(Guid sagaId, ISynchronizedStorageSession session, ContextBag context,
        CancellationToken cancellationToken = default) where TSagaData : class, IContainSagaData =>
        GetByQuery<TSagaData>(session, context, "Id = $id", ("$id", sagaId.ToString()), cancellationToken);

    public Task<TSagaData> Get<TSagaData>(string propertyName, object propertyValue,
        ISynchronizedStorageSession session, ContextBag context, CancellationToken cancellationToken = default)
        where TSagaData : class, IContainSagaData
    {
        ArgumentException.ThrowIfNullOrEmpty(propertyName);
        ArgumentNullException.ThrowIfNull(propertyValue);
        var value = Convert.ToString(propertyValue, CultureInfo.InvariantCulture) ?? "";
        return GetByQuery<TSagaData>(session, context, "CorrelationId = $val", ("$val", value), cancellationToken);
    }

    public async Task Complete(IContainSagaData sagaData, ISynchronizedStorageSession session, ContextBag context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sagaData);
        var sqliteSession = session.SqliteSession();
        var sagaDataType = sagaData.GetType();
        var info = sagaInfoCache.Get(sagaDataType);
        var oldConcurrency = RetrieveConcurrency(context, sagaData.Id, sagaDataType);

        await using var cmd = sqliteSession.CreateCommand();
        cmd.CommandText = $"DELETE FROM {info.TableName} WHERE Id = $id AND Concurrency = $old;";
        cmd.Parameters.AddWithValue("$id", sagaData.Id.ToString());
        cmd.Parameters.AddWithValue("$old", oldConcurrency);

        var deleted = await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        if (deleted != 1)
        {
            throw new InvalidOperationException(
                $"The saga of type '{sagaDataType.Name}' with id '{sagaData.Id}' could not be completed because it was updated by another process.");
        }
    }

    async Task<TSagaData> GetByQuery<TSagaData>(ISynchronizedStorageSession session, ContextBag context,
        string whereClause, (string Name, object Value) parameter, CancellationToken cancellationToken)
        where TSagaData : class, IContainSagaData
    {
        var sqliteSession = session.SqliteSession();
        var info = sagaInfoCache.Get(typeof(TSagaData));

        await using var cmd = sqliteSession.CreateCommand();
        cmd.CommandText = $"SELECT DataJson, Concurrency FROM {info.TableName} WHERE {whereClause};";
        cmd.Parameters.AddWithValue(parameter.Name, parameter.Value);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null!;
        }

        var dataJson = reader.GetString(0);
        var concurrency = reader.GetInt64(1);

        var sagaData = JsonSerializer.Deserialize<TSagaData>(dataJson, SerializerOptions)!;
        StashConcurrency(context, sagaData.Id, concurrency);
        return sagaData;
    }

    // Keyed by saga Id rather than saga type so two instances of the same saga type loaded in the
    // same pipeline (rare today but cheap to guard against) don't share a concurrency stash.
    static string ConcurrencyKey(Guid sagaId) =>
        $"NServiceBusContrib.Persistence.Sqlite.SagaConcurrency-{sagaId:N}";

    static void StashConcurrency(ContextBag context, Guid sagaId, long concurrency) =>
        context.Set(ConcurrencyKey(sagaId), concurrency);

    static long RetrieveConcurrency(ContextBag context, Guid sagaId, Type sagaDataType)
    {
        if (!context.TryGet<long>(ConcurrencyKey(sagaId), out var concurrency))
        {
            throw new InvalidOperationException(
                $"Saga concurrency was not captured for '{sagaDataType.Name}' with id '{sagaId}'. Get must be called before Update or Complete.");
        }
        return concurrency;
    }
}
