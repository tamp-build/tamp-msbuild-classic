# Changelog

All notable changes to `Tamp.MSBuildClassic` are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.2.0] - 2026-11-20 - .NET 11 in, .NET 8 and .NET 9 out

### Changed

- **Multi-targets `net10.0;net11.0`** instead of `net8.0;net9.0;net10.0` ([ADR 0015](https://github.com/tamp-build/tamp/blob/main/docs/adr/0015-target-framework-strategy.md)). .NET 11 shipped 2026-11-10 and .NET 8 and .NET 9 reached end of support the same day; the rule is to add a TFM the day a release ships and drop one the day Microsoft ends its support. `global.json` pins SDK 11.0.100.

  **If you target `net8.0` or `net9.0`, stay on the previous version** — this one carries no assets your project can resolve.

- **`Tamp.*` pinned to 1.21.0**, which carries a SARIF fix worth knowing about if this project's output is read as evidence: before 1.21.0, a SARIF result whose scanner omitted `level` was read as `none` — the least severe value — instead of the declared `warning`, on every runtime before net11 ([tamp#166](https://github.com/tamp-build/tamp/issues/166)).

- **Publishing moved to nuget.org trusted publishing (OIDC).** The `tamp_nuget_api_key` org secret expired 2026-11-01 along with every other pre-2026-08-17 nuget.org key. The publish job now mints a short-lived key from its own OIDC token, so there is no long-lived credential to rotate or leak.

## [0.1.2] — 2026-09-27

### Added

- Package now ships XML documentation files (`.xml`) alongside the assembly, so consumers get IntelliSense and API docs. (Mirrors [tamp-build/tamp#3](https://github.com/tamp-build/tamp/pull/50).)

### Fixed

- **TAM-270** — URL-encode `;` and `,` in `/p:Name=Value` and `/restoreProperty:Name=Value` argument values. MSBuild's CLI parses `;` and `,` as property-list separators inside a `/p:` token, so a raw value like `DefineConstants=TRACE;DEBUG` would fail with `MSB1006: Property is not valid. Switch: DEBUG`. Now emitted as `TRACE%3BDEBUG` per the canonical MSBuild escape. Applies to:
  - `Properties` dict entries
  - `RestoreProperties` dict entries
  - `Configuration` alias (defensive)
  - `Platform` alias (defensive)

## [0.1.0] — 2026-05-22

### Added

- Initial release. Typed wrappers for classic Windows `msbuild.exe` over the three highest-leverage verbs:
  - `Build` (`/t:Build`) — drives a project or solution build. Also exposes `WithImplicitRestore` for the `/restore` switch (single-invocation Restore + Build).
  - `Restore` (`/t:Restore`) — Restore target.
  - `Clean` (`/t:Clean`) — Clean target.
- Common knobs: `/p:Name=Value` properties, `/restoreProperty:Name=Value`, Configuration / Platform aliases, verbosity, `/m` max-cpu, `/nologo`, `/nodeReuse:false`, `/bl[:path]` binary log, `/ds` detailed summary.
- Parallel fluent + object-init authoring surface.
- Multi-target `net8.0;net9.0;net10.0`. Wrapper assemblies run cross-platform; `msbuild.exe` itself is Windows-only.
