namespace Messaging.Persistence.Sqlite.Sagas;

using System.Collections.Concurrent;

sealed class SagaInfoCache(string tablePrefix)
{
    readonly ConcurrentDictionary<Type, SagaInfo> cache = new();

    public SagaInfo Get(Type sagaDataType) =>
        cache.GetOrAdd(sagaDataType, t => new SagaInfo($"{tablePrefix}{t.Name}"));
}

sealed record SagaInfo(string TableName);
