namespace NServiceBusContrib.Persistence.Sqlite;

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

        // Only report values we know are accurate. Pragma-driven settings (journal mode,
        // busy timeout) are applied by DefaultConnectionFactory but a user-supplied
        // ConnectionFactory may not, so we don't claim them here.
        context.Settings.AddStartupDiagnosticsSection("NServiceBusContrib.Persistence.Sqlite", new
        {
            PackageVersion = typeof(SqlitePersistence).Assembly.GetName().Version?.ToString() ?? "unknown",
            TablePrefix = string.IsNullOrEmpty(tablePrefix) ? "(none)" : tablePrefix
        });
    }
}
