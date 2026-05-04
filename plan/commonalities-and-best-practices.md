# NServiceBus Persisters: Commonalities and Best Practices

This document captures patterns observed across the four reference persisters analysed in this workspace:

- `NServiceBus.Persistence.CosmosDB`
- `NServiceBus.Persistence.DynamoDB`
- `NServiceBus.Persistence.Sql` (MS SQL Server, PostgreSQL, MySQL, Oracle dialects)
- `NServiceBus.Storage.MongoDB`

It is intended as a checklist when designing a new persister (such as the SQLite one planned alongside this file).

## 1. Repository and Project Layout

A persister repository follows a consistent shape. The official Particular packages use `NServiceBus.Persistence.<Name>`; community persisters follow the same shape under their own (non-trademarked) prefix, e.g. `<Brand>.Persistence.<Name>`:

```
src/
  <Prefix>.Persistence.<Name>/                       (main package)
  <Prefix>.Persistence.<Name>.Tests/                 (unit tests)
  <Prefix>.Persistence.<Name>.PersistenceTests/      (NSB persistence test suite)
  <Prefix>.Persistence.<Name>.AcceptanceTests/       (one or more variants)
  <Prefix>.Persistence.<Name>.TransactionalSession/  (separate package)
  <Prefix>.Persistence.<Name>.TransactionalSession.AcceptanceTests/
```

Top-level repo files: `README.md`, `Package-README.md`, `LICENSE.md`, `CONTRIBUTING.md`, `SECURITY.md`, `global.json`, `nuget.config`.

Convention notes:

- Target framework: `net10.0`.
- `SignAssembly=true`, public types kept stable, internal types liberally used.
- `Particular.Analyzers` package referenced in every project.
- Nullable reference types enabled in newer persisters (DynamoDB, MongoDB); CosmosDB and SQL still partially opt in.
- `AllowUnsafeBlocks=true` is common (used for span manipulation, hashing, JSON serialisation paths).

## 2. The PersistenceDefinition Contract

Every persister exposes a single public type that derives from `PersistenceDefinition` and implements `IPersistenceDefinitionFactory<TSelf>`. Examples:

- `CosmosPersistence`
- `DynamoPersistence`
- `SqlPersistence`
- `MongoPersistence`

In its constructor it calls `Supports<StorageType.X>(s => s.EnableFeatureByDefault<FeatureType>())` for each storage type the persister implements. `StorageType.Sagas` can also pass a `SagasOptions { SupportsFinders = true }`.

Each enabled feature class:

- Derives from `Feature`.
- Sets `DependsOn<...>` and `Defaults(...)` to register settings keys (database name, table prefix, etc.).
- Overrides `Setup(FeatureConfigurationContext)` to register `IOutboxStorage`, `ISagaPersister`, `ISubscriptionStorage`, or `ICompletableSynchronizedStorageSession` factories with the DI container.

## 3. Storage Session Plumbing

A persister contributes its own `ICompletableSynchronizedStorageSession` implementation. Examples: `CosmosSynchronizedStorageSession`, `DynamoSynchronizedStorageSession`, `SqlSynchronizedStorageSession`, `SynchronizedStorageSession` (Mongo).

Responsibilities of the session:

1. Hold a transaction or batch object that is shared between saga writes, outbox writes, and user-initiated writes via the storage session.
2. Expose a typed accessor (e.g. `ISqlStorageSession`, `IDynamoStorageSession`) so user handlers can enlist their own writes in the same transaction.
3. Tie its lifetime to either a regular pipeline `ContextBag` or, for the outbox case, to the outbox transaction.

The pattern in all four is the same: `OutboxPersister.BeginTransaction` returns an `IOutboxTransaction` that wraps the same underlying transaction object the session adapts.

## 4. Outbox Pattern

The contract from NServiceBus.Outbox is identical for every persister:

1. `BeginTransaction(context)` opens a database transaction (or batch in NoSQL).
2. `Get(messageId, context)` returns null on a fresh message, returns a stored `OutboxMessage` on a duplicate.
3. `Store(message, transaction, context)` persists the operations; the transaction is committed by the caller.
4. `SetAsDispatched(messageId, context)` clears the stored operations and flags `Dispatched=true` (or sets a dispatched timestamp).
5. A cleanup hook removes dispatched records older than a configurable threshold.

Implementation choices that vary:

| Concern | CosmosDB | DynamoDB | SQL | MongoDB |
| --- | --- | --- | --- | --- |
| Dedup key | `id` (partition + id) | partition key + sort key | `MessageId` PK | composite `OutboxRecordId` |
| Operations storage | embedded array | items per operation | JSON column | embedded array |
| Concurrency | ETag | conditional write | unique constraint | unique index |
| Cleanup | TTL | manual scan | batched DELETE (4000 rows) | TTL index |

Common best practices:

- Make the cleanup interval and retention period configurable, with sane defaults (7 days dispatched retention is typical).
- Validate that `Store` happens inside the transaction returned by `BeginTransaction`; reject mismatched transactions with a clear exception.
- Always serialise operations as a discriminated payload (operation type + properties + headers + body) so future versions can evolve without breaking stored records. Add a `PersistenceVersion` field.

## 5. Saga Pattern

All four persisters implement optimistic concurrency by default; CosmosDB and DynamoDB additionally offer optional pessimistic locking.

- SQL and MongoDB use an integer version column (`Concurrency` and `_version`) and check it in the `WHERE`/filter clause on update.
- CosmosDB uses ETag conditional updates.
- DynamoDB uses a conditional expression on a version attribute.

Best practices:

- Always increment the version field as part of the same write that checks it.
- Translate concurrency violations into `Exception`s that callers can interpret as retryable. NServiceBus already retries on optimistic concurrency exceptions when wired correctly.
- Index the saga correlation property column. SQL maintains it as a real column, MongoDB relies on a compound index. Do not search by deserialising every saga document.
- Provide a clear `SagaNotFound` distinction between "no saga" (return null) and "wrong correlation property" (throw).
- Keep the saga storage format JSON-friendly. The data layer should never depend on the saga CLR shape, so additive saga changes do not break existing rows.

## 6. Subscription Storage

Only SQL and MongoDB implement subscription storage; CosmosDB and DynamoDB rely on transport-managed pub/sub.

Patterns:

- Store one row per `(MessageType, TransportAddress, Endpoint)` triple.
- Look up by an `IN` over the message-type hierarchy.
- Optional in-memory cache with TTL (SQL exposes a `cacheFor` parameter); cache invalidates per Subscribe/Unsubscribe call.
- Subscribe is idempotent: insert-or-ignore, never throw on duplicates.

## 7. TransactionalSession Integration

Each persister ships a sibling package (`<Prefix>.Persistence.<Name>.TransactionalSession`) exposing a single extension on `PersistenceExtensions<TPersistence>`:

```csharp
EnableTransactionalSession(this PersistenceExtensions<TPersistence> persistence,
                           TransactionalSessionOptions options = null)
```

Responsibilities of the package:

1. Register a `Feature` that wires the persister-specific outbox transaction factory into the transactional session.
2. Register a control-message handler so processor endpoints can complete sessions opened on send-only endpoints.
3. Expose a typed `OpenSession` extension on `ITransactionalSession` if the persister has a session-typed accessor.

Notes:

- Support both an in-process (no processor endpoint) and an explicit `ProcessorEndpoint` configuration.
- The same outbox infrastructure is reused; the transactional session is essentially a long-running outbox transaction outside of message handling.
- Acceptance tests for this package always include "send via session, expect handler to run" and "abandon session, expect nothing to happen".

## 8. Configuration Extensibility

Configuration is exposed as static methods on a `*PersistenceConfig` (or `*PersistenceExtensions`) class taking `PersistenceExtensions<TPersistence>`.

Common knobs:

- Connection / client object (pre-configured) or connection string.
- Database name (or container/table prefix).
- Disable installer (so deployment owns schema).
- Sagas sub-config (pessimistic locking, lease durations).
- Outbox cleanup configuration (frequency, retention).
- Custom serializer / JSON settings.

Best practices:

- Settings are stored on `PersistenceExtensions.GetSettings()` under string keys defined as `internal const string` fields. Centralise these constants so feature classes and config extensions stay in sync.
- Provide a `TransactionInformation` configuration for multi-tenant scenarios (Cosmos, DynamoDB) so users can extract a partition key from headers/messages.
- Avoid leaking the underlying client type to user code unless necessary; expose a typed wrapper through the storage session.

## 9. Installers and Schema Management

The SQL persister generates DDL scripts at build time (MSBuild task), then `SetupAndTeardownDatabase : FeatureStartupTask` runs them per endpoint (in tests). CosmosDB and DynamoDB ship installers that create containers/tables on startup. MongoDB creates collections and indexes lazily at first write.

Schema-creating code that depends on the host running it should be wired through `INeedToInstallSomething`. The host only invokes installers when the user has called `endpointConfiguration.EnableInstallers()`. This is the canonical opt-in: production endpoints typically leave installers disabled and schema creation is handled by deployment tooling, while dev/test endpoints opt in.

Best practices:

- Use `INeedToInstallSomething` for schema creation rather than feature startup tasks. Without `EnableInstallers()`, the installer is never invoked, which is the correct production default.
- Do not invent a separate `DisableTableCreation`-style opt-out; absence of `EnableInstallers()` already provides it.
- Make installer code idempotent: `IF NOT EXISTS`, `CreateIfNotExists`, etc., so repeated dev runs are safe.
- Have features stash the metadata they need (saga-to-table mappings, etc.) in settings so the installer can read it without re-running feature setup logic.
- Maintain an indexable schema version per table/collection so future migrations can be detected.

## 10. Testing Strategy

Three test layers per persister:

- **Unit tests** (`*.Tests`) targeting individual classes; no real database.
- **NServiceBus persistence tests** (`*.PersistenceTests`) implementing the standard NSB persistence contract suite.
- **Acceptance tests** (`*.AcceptanceTests`) wiring up the full NSB pipeline with this persister; one or more variants per feature axis (logical vs physical outbox, optimistic vs pessimistic sagas, eventually consistent reads, etc.).

Best practices:

- Provision an isolated database/schema/prefix per test run (often based on a `testId`). Use a `SemaphoreSlim` per `testId` to keep parallel runs from racing on schema setup.
- Clean up via a `FeatureStartupTask` registered into the endpoint, not via test fixtures; this ensures parity with how the real persister would shut down.
- Wire a `RunSettings` extension method (e.g. `UseSqlPersistence`) so individual acceptance tests stay readable.
- Acceptance tests should rely only on public extension methods to configure persistence; this validates the public surface.

## 11. Diagnostics and Observability

- Persisters write a custom diagnostics blob via `IFeatureDiagnostics` on startup, listing dialect, connection string source, version.
- All asynchronous APIs accept `CancellationToken` and pass it through to driver calls.
- Logs are structured via `LoggerFactory.GetLogger<T>()` from NServiceBus, never `Console.WriteLine`.

## 12. Versioning and Release

- Each persister follows SemVer; tags are pushed only by maintainers.
- A `PersistenceVersion` constant is embedded in stored records (outbox, sagas) so future code knows which schema produced a row.
- Public API surface is locked down with `PublicApiTests` (Microsoft.CodeAnalysis.PublicApiAnalyzers + shipped/unshipped txt files); changes to the public surface require explicit acknowledgement.

## 13. Cross-cutting Best Practices

- **Idempotency everywhere**: outbox, subscribe, installer, cleanup. Replays must be safe.
- **Fail closed on transaction mismatch**: never silently let a write happen outside the expected transaction.
- **Never rely on serialised CLR types**: only persist JSON or the database-native equivalent.
- **Cancellation is non-optional**: every public async method takes a `CancellationToken` and passes it down.
- **Sealed where possible**: feature classes, persisters, and config types are `sealed` unless inheritance is part of the contract.
- **Internal-by-default**: keep the public surface tiny. Anything users do not configure should be `internal`.
- **Constants for setting keys**: setting bag keys are declared as `internal const string` to avoid drift between writer and reader.
- **No DTC**: NServiceBus persisters target `TransactionScope`-free workflows. Coordinate via the outbox or storage session, not distributed transactions.
