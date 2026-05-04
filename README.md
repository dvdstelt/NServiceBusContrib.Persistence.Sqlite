# Messaging.Persistence.Sqlite

A community SQLite persistence package for [NServiceBus](https://particular.net/nservicebus) 10.

This project is not affiliated with or endorsed by Particular Software. NServiceBus is a registered trademark of Particular Software; the name is used here in nominative reference only.

## What it provides

- Outbox storage
- Saga storage with optimistic concurrency
- Subscription storage
- Synchronized storage session sharing one `SqliteConnection` and `SqliteTransaction`
- TransactionalSession support (sibling package `Messaging.Persistence.Sqlite.TransactionalSession`)

## Status

Pre-release scaffolding. See [plan/sqlite-persister-plan.md](plan/sqlite-persister-plan.md) for the implementation roadmap and [plan/commonalities-and-best-practices.md](plan/commonalities-and-best-practices.md) for the conventions this persister follows.

## Building

```sh
dotnet build
dotnet test
```

Requires the .NET 10 SDK.

## License

MIT. See [LICENSE.md](LICENSE.md).
