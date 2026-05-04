namespace Messaging.Persistence.Sqlite;

using Microsoft.Extensions.DependencyInjection;
using NServiceBus.Features;
using NServiceBus.Persistence;

sealed class SynchronizedStorageFeature : Feature
{
    public SynchronizedStorageFeature() => DependsOn<SynchronizedStorage>();

    protected override void Setup(FeatureConfigurationContext context)
    {
        var connectionFactory = SqliteSettings.ResolveConnectionFactory(context.Settings);
        var tablePrefix = SqliteSettings.ResolveTablePrefix(context.Settings);

        context.Services.AddSingleton(connectionFactory);

        context.Services.AddScoped<ICompletableSynchronizedStorageSession>(sp =>
            new SqliteSynchronizedStorageSession(sp.GetRequiredService<IConnectionFactory>()));

        context.Services.AddScoped(sp =>
            (sp.GetRequiredService<ICompletableSynchronizedStorageSession>() as ISqliteStorageSession)!);

        context.Settings.AddStartupDiagnosticsSection("Messaging.Persistence.Sqlite", new
        {
            PackageVersion = typeof(SqlitePersistence).Assembly.GetName().Version?.ToString() ?? "unknown",
            TablePrefix = string.IsNullOrEmpty(tablePrefix) ? "(none)" : tablePrefix,
            JournalMode = "WAL",
            BusyTimeoutMs = 5000
        });
    }
}
