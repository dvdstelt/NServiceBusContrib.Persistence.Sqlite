namespace Messaging.Persistence.Sqlite.Sagas;

using System.Collections.Concurrent;
using System.Text.RegularExpressions;

sealed class SagaInfoCache(string tablePrefix)
{
    static readonly Regex IdentifierPattern = new("^[A-Za-z][A-Za-z0-9_]*$", RegexOptions.Compiled);

    readonly ConcurrentDictionary<Type, SagaInfo> cache = new();

    public SagaInfo Get(Type sagaDataType) =>
        cache.GetOrAdd(sagaDataType, t =>
        {
            // Type.Name for a generic saga type comes back as "MyState`1", which would break
            // unquoted DDL/DML. Reject anything that isn't a plain identifier so the failure is
            // visible at endpoint startup rather than as an opaque SQL parse error later.
            if (!IdentifierPattern.IsMatch(t.Name))
            {
                throw new ArgumentException(
                    $"Saga data type '{t.Name}' is not a supported identifier. Generic saga data types are not supported by the SQLite persister; use a non-generic class.",
                    nameof(sagaDataType));
            }
            return new SagaInfo($"{tablePrefix}{t.Name}");
        });
}

sealed record SagaInfo(string TableName);
