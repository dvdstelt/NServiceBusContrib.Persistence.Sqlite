namespace Messaging.Persistence.Sqlite;

using NServiceBus.Settings;

/// <summary>
/// Outbox-specific configuration for the SQLite persister.
/// </summary>
public sealed class OutboxConfiguration
{
    readonly SettingsHolder settings;

    internal OutboxConfiguration(SettingsHolder settings) => this.settings = settings;

    /// <summary>
    /// How long dispatched outbox records are kept before the cleanup task removes them.
    /// </summary>
    public OutboxConfiguration KeepDeduplicationDataFor(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(duration), "Retention period must be positive.");
        }
        settings.Set(SettingsKeys.OutboxRetentionPeriod, duration);
        return this;
    }

    /// <summary>
    /// How often the cleanup task runs to remove dispatched outbox records past their retention period.
    /// </summary>
    public OutboxConfiguration FrequencyToRunDeduplicationDataCleanup(TimeSpan frequency)
    {
        if (frequency <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(frequency), "Cleanup frequency must be positive.");
        }
        settings.Set(SettingsKeys.OutboxCleanupFrequency, frequency);
        return this;
    }
}
