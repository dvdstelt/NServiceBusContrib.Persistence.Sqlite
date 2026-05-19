# NServiceBusContrib.Persistence.Sqlite: Implementation Plan

## Overview

Build a new persister, `NServiceBusContrib.Persistence.Sqlite`, for NServiceBus 10. It targets file-based and in-memory SQLite databases via `Microsoft.Data.Sqlite`. The package name avoids the `NServiceBus.*` and `Particular.*` prefixes, which are trademarked and reserved for Particular Software. The README and NuGet description identify it as a community persister for NServiceBus (nominative use). The persister supports:

- Outbox storage
- Saga storage (optimistic concurrency)
- Subscription storage
- Synchronized storage session (sharing one `SqliteConnection` and `SqliteTransaction` between outbox, sagas, subscriptions, and user handler writes)
- TransactionalSession (separate package `NServiceBusContrib.Persistence.Sqlite.TransactionalSession`)

The companion document, `commonalities-and-best-practices.md`, captures the cross-cutting patterns this plan relies on.

## Key Constraints

- **Single-writer semantics**: SQLite serialises writers at the database level. The plan assumes WAL journal mode for concurrent readers and a single writer at a time. It does not attempt MS SQL Server style row-level locking.
- **No distributed transactions**: NServiceBus persisters do not use DTC. All cross-storage atomicity comes from a single `SqliteTransaction` shared via the storage session.
- **Schema ownership**: schema creation is gated by NServiceBus's `EnableInstallers()` endpoint configuration. The persister implements `INeedToInstallSomething`, which the host only invokes when `endpointConfiguration.EnableInstallers()` is called (typically in dev/test). When installers are not enabled, the persister assumes the schema already exists and never runs DDL. This matches the convention in `NServiceBus.Persistence.Sql` and the other reference persisters.
- **Target framework**: `net10.0`, `Microsoft.Data.Sqlite` 9.x, `NServiceBus` 10.1.x (matching the other persisters in the workspace).
- **JSON storage**: payloads (saga data, outbox operations) are stored as `TEXT` columns containing JSON. SQLite 3.38+ has native JSON functions, but this persister will treat the columns as opaque text and serialise/deserialise in C# to keep the data layer portable.
- **Concurrency model for sagas**: optimistic only in v1. Pessimistic locking via `BEGIN IMMEDIATE` could be added later.
- **Multi-tenancy**: out of scope for v1. One database per endpoint or shared database with per-endpoint table prefix.
- **No timeout storage**: NServiceBus 10 expects timeouts from the transport layer.

## Why a Standalone Persister, Not a New SQL Dialect

The existing `NServiceBus.Persistence.Sql` codebase has a `SqlDialect` abstraction with concrete implementations for MS SQL Server, PostgreSQL, MySQL, and Oracle. Adding a `SqlDialect_Sqlite` would reuse the script builders and command builders.

The plan recommends a **standalone persister** for these reasons:

1. SQLite ships in-process. Connection lifetime, pooling, and journal-mode handling are quite different from the server-based dialects, particularly around `:memory:` databases used in tests.
2. SQLite has no row-level locking. Trying to fit it into the existing pessimistic-locking codepaths in `NServiceBus.Persistence.Sql` would add conditional branches throughout the dialect.
3. The packaging story (single NuGet, no driver dependency tree) is cleaner standalone.
4. The codebase here (`NServiceBusContrib.Persistence.Sqlite`) is already structured as a separate persister.

Alternative (deferred): once v1 is shipped, evaluate folding it back into `NServiceBus.Persistence.Sql` as a fifth dialect if that proves valuable.

## High-Level Architecture

```
NServiceBusContrib.Persistence.Sqlite (main package)
  Configuration:
    SqlitePersistence : PersistenceDefinition
    SqlitePersistenceConfig (static config extensions)
    SqlitePersistenceSettings (typed settings wrapper)

  Storage session:
    SqliteSynchronizedStorageSession : ICompletableSynchronizedStorageSession
    ISqliteStorageSession (typed user-facing accessor)

  Outbox:
    OutboxFeature : Feature
    OutboxPersister : IOutboxStorage
    OutboxTransaction : IOutboxTransaction
    OutboxRecord (POCO with Id, Dispatched, DispatchedAt, OperationsJson, PersistenceVersion)
    OutboxCleaner : FeatureStartupTask (periodic DELETE)

  Sagas:
    SagaFeature : Feature
    SagaPersister : ISagaPersister (optimistic concurrency on Concurrency column)
    SagaInfoCache (per-saga-type table name + correlation column metadata)

  Subscriptions:
    SubscriptionFeature : Feature
    SubscriptionPersister : ISubscriptionStorage
    SubscriptionCache (optional, ConcurrentDictionary with TTL)

  Installer (runs only when EnableInstallers() is set on the endpoint):
    Installer : INeedToInstallSomething (creates tables; never runs unless enabled)
    SchemaScripts (static class returning DDL strings)

  Connection management:
    IConnectionFactory (creates SqliteConnection)
    DefaultConnectionFactory (uses configured connection string)
    ConnectionScope (Open + apply pragmas, e.g. journal_mode=WAL, foreign_keys=ON)

NServiceBusContrib.Persistence.Sqlite.TransactionalSession (sibling package)
  SqliteTransactionalSessionExtensions.EnableTransactionalSession
  SqliteTransactionalSession : Feature
  Control message customizations
```

## Schema (DDL)

All tables live in the same database file. Default `TablePrefix` is empty; users may set one for shared databases. JSON columns are plain `TEXT` and serialised in C#.

```sql
CREATE TABLE IF NOT EXISTS {prefix}OutboxRecord (
    MessageId           TEXT    NOT NULL PRIMARY KEY,
    Dispatched          INTEGER NOT NULL DEFAULT 0,
    DispatchedAt        TEXT    NULL,
    OperationsJson      TEXT    NOT NULL,
    PersistenceVersion  TEXT    NOT NULL
) WITHOUT ROWID;

CREATE INDEX IF NOT EXISTS IX_{prefix}OutboxRecord_DispatchedAt
    ON {prefix}OutboxRecord (DispatchedAt) WHERE Dispatched = 1;

CREATE TABLE IF NOT EXISTS {prefix}{SagaName} (
    Id                 TEXT    NOT NULL PRIMARY KEY,
    DataJson           TEXT    NOT NULL,
    Metadata           TEXT    NOT NULL,
    CorrelationId      TEXT    NULL,
    Concurrency        INTEGER NOT NULL DEFAULT 0,
    PersistenceVersion TEXT    NOT NULL
) WITHOUT ROWID;

CREATE UNIQUE INDEX IF NOT EXISTS UX_{prefix}{SagaName}_CorrelationId
    ON {prefix}{SagaName} (CorrelationId) WHERE CorrelationId IS NOT NULL;

CREATE TABLE IF NOT EXISTS {prefix}SubscriptionRecord (
    MessageType      TEXT NOT NULL,
    Subscriber       TEXT NOT NULL,
    Endpoint         TEXT NULL,
    PRIMARY KEY (MessageType, Subscriber)
) WITHOUT ROWID;
```

Notes:

- `WITHOUT ROWID` reduces storage overhead for small composite primary keys.
- Dates are stored as ISO-8601 `TEXT` for portability (no timezone ambiguity if always UTC).
- The saga table is named per saga type, mirroring the SQL persister convention.
- Pragmas applied per connection: `journal_mode=WAL`, `synchronous=NORMAL`, `foreign_keys=ON`, `busy_timeout=5000`.

## Public API Surface (sketch)

```csharp
// Main persistence entry point
public sealed class SqlitePersistence : PersistenceDefinition,
    IPersistenceDefinitionFactory<SqlitePersistence>
{
    public SqlitePersistence()
    {
        Supports<StorageType.Outbox>(s => s.EnableFeatureByDefault<OutboxFeature>());
        Supports<StorageType.Sagas>(s => s.EnableFeatureByDefault<SagaFeature>());
        Supports<StorageType.Subscriptions>(s => s.EnableFeatureByDefault<SubscriptionFeature>());
    }
}

public static class SqlitePersistenceConfig
{
    public static PersistenceExtensions<SqlitePersistence> ConnectionString(
        this PersistenceExtensions<SqlitePersistence> persistence, string connectionString);

    public static PersistenceExtensions<SqlitePersistence> ConnectionFactory(
        this PersistenceExtensions<SqlitePersistence> persistence,
        Func<CancellationToken, ValueTask<SqliteConnection>> factory);

    public static PersistenceExtensions<SqlitePersistence> TablePrefix(
        this PersistenceExtensions<SqlitePersistence> persistence, string prefix);

    public static PersistenceExtensions<SqlitePersistence> SubscriptionsCacheFor(
        this PersistenceExtensions<SqlitePersistence> persistence, TimeSpan duration);

    public static OutboxConfiguration Outbox(
        this PersistenceExtensions<SqlitePersistence> persistence);
}

public sealed class OutboxConfiguration
{
    public OutboxConfiguration KeepDeduplicationDataFor(TimeSpan duration);
    public OutboxConfiguration FrequencyToRunDeduplicationDataCleanup(TimeSpan frequency);
}

// Storage session accessor for user handlers
public interface ISqliteStorageSession
{
    SqliteConnection Connection { get; }
    SqliteTransaction Transaction { get; }
    void OnSaveChanges(Func<ISqliteStorageSession, CancellationToken, Task> callback);
}
```

## Phased Plan

Each phase ends in a buildable, testable state. Steps are ordered so unit tests precede integration tests where practical.

### Phase 1: Repository Scaffolding

- [ ] Add `src/NServiceBusContrib.Persistence.Sqlite/NServiceBusContrib.Persistence.Sqlite.csproj` targeting `net10.0`, with `Microsoft.Data.Sqlite` and `NServiceBus` package references plus `Particular.Analyzers`.
- [ ] Add `src/NServiceBusContrib.Persistence.Sqlite.Tests/` (unit tests, NUnit).
- [ ] Add `src/NServiceBusContrib.Persistence.Sqlite.PersistenceTests/` referencing the NServiceBus persistence test pack.
- [ ] Add `src/NServiceBusContrib.Persistence.Sqlite.AcceptanceTests/` referencing `NServiceBus.AcceptanceTests` shared sources.
- [ ] Add `src/NServiceBusContrib.Persistence.Sqlite.TransactionalSession/` and matching `*.AcceptanceTests`.
- [ ] Add a top-level solution file, `Directory.Build.props`, `Directory.Packages.props` (centralised package versions).
- [ ] Add `global.json` pinned to .NET 10 with `rollForward=latestFeature`.
- [ ] Add boilerplate top-level docs (`README.md`, `Package-README.md`, `LICENSE.md`, `CONTRIBUTING.md`, `SECURITY.md`).
- [ ] Wire `Particular.Analyzers`, `Microsoft.CodeAnalysis.PublicApiAnalyzers`, and shipped/unshipped public API tracking files.

### Phase 2: Configuration and Settings Plumbing

- [ ] Implement `SqlitePersistence : PersistenceDefinition`. Wire `Supports<StorageType.X>` for Outbox, Sagas, Subscriptions.
- [ ] Define `internal static class SettingsKeys` with constants: `ConnectionString`, `ConnectionFactory`, `TablePrefix`, `DisableTableCreation`, `SubscriptionsCacheFor`, `OutboxRetentionPeriod`, `OutboxCleanupFrequency`.
- [ ] Implement `SqlitePersistenceConfig` extension methods writing into `persistence.GetSettings()`.
- [ ] Implement `OutboxConfiguration` and corresponding settings keys.
- [ ] Implement `IConnectionFactory` and `DefaultConnectionFactory` (opens connection, applies pragmas).
- [ ] Add unit tests covering config validation (empty connection string rejected, prefix validated against SQL injection patterns, cache duration non-negative).

### Phase 3: Storage Session

- [ ] Implement `SqliteSynchronizedStorageSession : ICompletableSynchronizedStorageSession`. Hold a `SqliteConnection`, `SqliteTransaction`, and a list of `OnSaveChanges` callbacks.
- [ ] Implement `ISqliteStorageSession` accessor exposed via `context.SynchronizedStorageSession.SqliteSession()`.
- [ ] Implement `SynchronizedStorageFeature : Feature` registering the session factory.
- [ ] Decide ownership: when the outbox is enabled, the outbox transaction creates the connection and transaction; otherwise the session creates them. Mirror the SQL persister design here (`SqlSynchronizedStorageSession` has both modes).
- [ ] Unit tests: dispose semantics, double-complete is a no-op, disposing without complete rolls back.

### Phase 4: Outbox Storage

- [ ] Implement `OutboxRecord` POCO and `OutboxOperation` serialisation contract (header + body bytes as base64, transport address, message intent enum, deliveryconstraints metadata).
- [ ] Implement `OutboxPersister : IOutboxStorage`:
  - [ ] `BeginTransaction(context)` opens connection, begins `BEGIN IMMEDIATE` transaction, returns `OutboxTransaction`.
  - [ ] `Get(messageId, context)` selects record by PK; returns null when absent.
  - [ ] `Store(message, transaction, context)` inserts record. On `SQLITE_CONSTRAINT_PRIMARYKEY`, throws a typed `OutboxConcurrencyException` (NSB will retry).
  - [ ] `SetAsDispatched(messageId, context)` updates `Dispatched=1`, `DispatchedAt=now`, `OperationsJson=NULL`.
- [ ] Implement `OutboxFeature : Feature` registering the persister and starting `OutboxCleaner`. Schema creation is the installer's job, not the feature's.
- [ ] Implement `OutboxCleaner : FeatureStartupTask` running on the configured frequency, deleting in batches of N rows where `Dispatched=1 AND DispatchedAt < @cutoff`.
- [ ] Unit tests: round-trip a record, dispatched marks operations null, duplicate-store throws.

### Phase 5: Saga Storage

- [ ] Implement `SagaPersister : ISagaPersister`:
  - [ ] Save: `INSERT` with `Concurrency=1`. Capture row count.
  - [ ] Update: `UPDATE ... WHERE Id=@id AND Concurrency=@oldConcurrency`. If row count is zero, throw an optimistic concurrency exception.
  - [ ] Get by id: `SELECT ... WHERE Id=@id`.
  - [ ] Get by correlation property: `SELECT ... WHERE CorrelationId=@value`. Cache the saga-to-table map per saga type.
  - [ ] Complete: `DELETE WHERE Id=@id AND Concurrency=@oldConcurrency`.
- [ ] Implement saga JSON serialisation using `System.Text.Json` with options that ignore null and write indented=false.
- [ ] Implement `SagaFeature : Feature`. On feature startup, gather saga types via `SettingsKeys.Sagas`, map each to its table name, and stash the metadata for the installer to read. Do not run DDL here; that is the installer's job, gated by `EnableInstallers()`.
- [ ] Unit tests: optimistic conflict exception, correlation lookup, save then update sequence.

### Phase 6: Subscription Storage

- [ ] Implement `SubscriptionPersister : ISubscriptionStorage` with `Subscribe`, `Unsubscribe`, `GetSubscriberAddressesForMessage`.
- [ ] Use `INSERT OR IGNORE` for idempotent subscribe, `DELETE` for unsubscribe.
- [ ] Implement optional in-memory cache (concurrent dictionary, TTL) keyed by message-type tuple. Invalidate on subscribe/unsubscribe.
- [ ] Implement `SubscriptionFeature : Feature`. DDL is owned by the installer, not by the feature.
- [ ] Unit tests: hierarchy lookup, cache invalidation, idempotent subscribe.

### Phase 7: Installer and Schema Scripts

- [ ] Implement `Installer : INeedToInstallSomething`. On install, open a connection and run all DDL scripts inside a transaction. The host only invokes this when the user has called `endpointConfiguration.EnableInstallers()`; without it, the installer is never run and the persister assumes the schema already exists.
- [ ] Centralise DDL strings in `static class SchemaScripts` with one method per table type, accepting the table prefix and saga/correlation metadata.
- [ ] Validate the table prefix against `^[A-Za-z0-9_]+$` to block injection attempts.
- [ ] Tests against a `:memory:` connection: installer is idempotent (run twice, no error).
- [ ] Test that, with installers disabled, querying a missing table surfaces a clear error rather than silently creating it.

### Phase 8: Diagnostics and Polish

- [ ] Wire `IFeatureDiagnostics` to emit a JSON blob describing the persister: package version, SQLite version (`SELECT sqlite_version()`), journal mode, table prefix.
- [ ] Add NServiceBus-style logging via `LogManager.GetLogger<...>()` for installer activity, cleanup batches, optimistic conflicts.
- [ ] Ensure every public async method takes a `CancellationToken` and forwards it to `ExecuteNonQueryAsync` etc.
- [ ] Ensure all classes that should be sealed are sealed.

### Phase 9: Acceptance Tests

- [ ] Implement `RunSettings` extension `UseSqlitePersistence` so individual tests stay readable.
- [ ] Implement `SetupAndTeardownDatabase : FeatureStartupTask` that creates a per-test database file (or `:memory:` shared cache with a unique name) and tears it down. Use `SemaphoreSlim` per test name to guard parallel runs.
- [ ] Wire the standard NServiceBus acceptance test suite. Verify outbox, saga, subscription scenarios pass.
- [ ] Add a SQLite-specific test variant that runs against an on-disk file (validates persistence across restart).

### Phase 10: TransactionalSession Package

- [ ] Create `NServiceBusContrib.Persistence.Sqlite.TransactionalSession` project.
- [ ] Implement `SqliteTransactionalSession : Feature` registering the outbox-transaction-based session source.
- [ ] Implement `SqliteTransactionalSessionExtensions.EnableTransactionalSession(this PersistenceExtensions<SqlitePersistence>, TransactionalSessionOptions)`.
- [ ] Provide `OpenSession` typed API returning `ITransactionalSession`.
- [ ] Acceptance tests:
  - [ ] Open session, send messages, commit: handler runs.
  - [ ] Open session, send messages, dispose without commit: nothing runs.
  - [ ] Open session in non-handler context (e.g. ASP.NET request) using a processor endpoint.
  - [ ] Verify control-message handshake when a processor endpoint is configured.

### Phase 11: Documentation

- [ ] Write `README.md` covering installation, configuration, supported scenarios, and tested SQLite versions.
- [ ] Write `Package-README.md` for NuGet.
- [ ] Add an upgrade-guide stub for future versions.
- [ ] Document the schema in `docs/schema.md` so DBAs (and future migrations) have a reference.

## Risk Register

| Risk | Mitigation |
| --- | --- |
| Single-writer bottleneck under load | Document the trade-off; recommend WAL + short transactions; suggest server-class persisters for high concurrency. |
| `Microsoft.Data.Sqlite` connection pooling pitfalls | Pin `Pooling=True` defaults and document `:memory:` shared cache patterns for tests. |
| Schema drift between persister versions | Embed a `PersistenceVersion` value in every record and assert compatibility on read. |
| Cleanup batches stalling writers (DELETE holds lock) | Run cleanup in small batches with `LIMIT`, sleep between batches if needed. |
| Misuse of TransactionalSession when outbox disabled | Throw a clear startup exception requiring outbox to be enabled with TransactionalSession (consistent with other persisters). |
| Saga JSON breaking on schema evolution | Use additive-only JSON (no required fields beyond `Id`); document conventions in `docs/schema.md`. |

## Out of Scope (v1)

- Pessimistic saga locking.
- Multi-tenant partitioning.
- Native SQLite JSON1 query support for saga lookup.
- Migration tooling between schema versions.
- Build-time DDL generation MSBuild task. (Runtime installer is sufficient for SQLite.)
- Distributed scenarios involving multiple writer processes against the same file. (Documented as unsupported.)

## Open Questions

1. Should outbox cleanup also `VACUUM` periodically, or leave that to operators? Recommend leaving it to operators in v1.
2. Should the persister expose a hook to plug in a custom JSON serialiser (System.Text.Json options)? Likely yes; mirror the SQL persister's `JsonSerializer` extension point.
3. Should `:memory:` be a first-class supported scenario for production, or only for tests? Recommend tests-only and document accordingly.
4. Should saga type to table-name mapping support custom names (e.g. attribute-driven) or always be `{prefix}{SagaTypeName}`? Start with the convention; add an attribute in v1.1 if requested.
