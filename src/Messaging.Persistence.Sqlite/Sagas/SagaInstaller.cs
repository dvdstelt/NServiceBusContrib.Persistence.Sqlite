namespace Messaging.Persistence.Sqlite.Sagas;

using System.Reflection;
using NServiceBus;
using NServiceBus.Installation;

sealed class SagaInstaller(IConnectionFactory connectionFactory, SagaInfoCache sagaInfoCache) : INeedToInstallSomething
{
    public async Task Install(string identity, CancellationToken cancellationToken = default)
    {
        var sagaTypes = DiscoverSagaTypes();
        await using var connection = await connectionFactory.OpenConnection(cancellationToken).ConfigureAwait(false);

        foreach (var sagaType in sagaTypes)
        {
            var info = sagaInfoCache.Get(sagaType);
            await using var command = connection.CreateCommand();
            command.CommandText = SchemaScripts.CreateSagaTable(info.TableName);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    static IEnumerable<Type> DiscoverSagaTypes() =>
        AppDomain.CurrentDomain.GetAssemblies()
            .Where(assembly => !assembly.IsDynamic)
            .SelectMany(SafeGetTypes)
            .Where(type => typeof(IContainSagaData).IsAssignableFrom(type)
                           && type is { IsClass: true, IsAbstract: false, ContainsGenericParameters: false }
                           && type != typeof(ContainSagaData))
            .Distinct();

    static IEnumerable<Type> SafeGetTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t is not null)!;
        }
    }
}
