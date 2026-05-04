# Contributing

Contributions are welcome. Please open an issue first for any non-trivial change so the design can be discussed before code is written.

## Development

- .NET 10 SDK
- `dotnet build` from the repo root
- `dotnet test` to run all test projects

## Conventions

- Code style is enforced via `.editorconfig` and `EnforceCodeStyleInBuild`. Warnings are treated as errors in Release builds.
- Public API additions must be tracked in `PublicAPI.Unshipped.txt` and moved to `PublicAPI.Shipped.txt` on release (once the analyzer is wired in).
- Follow the patterns documented in [plan/commonalities-and-best-practices.md](plan/commonalities-and-best-practices.md).

## Trademark

This project uses the name `NServiceBus` only in nominative reference (i.e. "for NServiceBus"). Pull requests that put `NServiceBus`, `Particular`, or `NSB` into package ids, namespaces, type names, or branding will be declined.
