# Messaging.Persistence.Sqlite

A community SQLite persistence package for [NServiceBus](https://particular.net/nservicebus) 10.

This project is not affiliated with or endorsed by Particular Software. NServiceBus is a registered trademark of Particular Software; the name is used here in nominative reference only.

## What it provides

- Outbox storage
- Saga storage with optimistic concurrency
- Subscription storage
- Synchronized storage session sharing one `SqliteConnection` and `SqliteTransaction`
- TransactionalSession support (sibling package `NServiceBusContrib.Persistence.Sqlite.TransactionalSession`)

## Status

Pre-release scaffolding. See [plan/sqlite-persister-plan.md](plan/sqlite-persister-plan.md) for the implementation roadmap and [plan/commonalities-and-best-practices.md](plan/commonalities-and-best-practices.md) for the conventions this persister follows.

## Building

```sh
dotnet build
dotnet test
```

Requires the .NET 10 SDK.

## NuGet packages

This repository publishes two packages:

- `NServiceBusContrib.Persistence.Sqlite` - Outbox, Saga, Subscription, and synchronized storage session support.
- `NServiceBusContrib.Persistence.Sqlite.TransactionalSession` - TransactionalSession support; depends on the package above.

### Local pack

```sh
dotnet pack -c Release -o artifacts
```

Produces `.nupkg` and `.snupkg` files in `./artifacts`. The package version is derived from git tags by [MinVer](https://github.com/adamralph/minver); without a reachable tag the version will be `0.1.0-beta.0.<height>`.

### Releasing to nuget.org

Releases are driven by SemVer-shaped git tags via the `release` GitHub Actions workflow:

1. Make sure CI is green on the default branch.
2. Create a SemVer tag locally, for example `git tag 0.1.0-beta.1` (no `v` prefix, MinVer reads the bare version).
3. Push the tag: `git push origin 0.1.0-beta.1`. The workflow builds, tests, packs, and pushes both `.nupkg` and `.snupkg` files to nuget.org using the `NUGET_API_KEY` repository secret.

To set up the secret: in repository Settings -> Secrets and variables -> Actions, add `NUGET_API_KEY` with a nuget.org API key scoped to push the two package IDs above.

## License

MIT. See [LICENSE.md](LICENSE.md).
