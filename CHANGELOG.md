# Changelog

All notable changes to `Tamp.MSBuildClassic` are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.1.0] — Unreleased

### Added

- Initial release. Typed wrappers for classic Windows `msbuild.exe` over the three highest-leverage verbs:
  - `Build` (`/t:Build`) — drives a project or solution build. Also exposes `WithImplicitRestore` for the `/restore` switch (single-invocation Restore + Build).
  - `Restore` (`/t:Restore`) — Restore target.
  - `Clean` (`/t:Clean`) — Clean target.
- Common knobs: `/p:Name=Value` properties, `/restoreProperty:Name=Value`, Configuration / Platform aliases, verbosity, `/m` max-cpu, `/nologo`, `/nodeReuse:false`, `/bl[:path]` binary log, `/ds` detailed summary.
- Parallel fluent + object-init authoring surface.
- Multi-target `net8.0;net9.0;net10.0`. Wrapper assemblies run cross-platform; `msbuild.exe` itself is Windows-only.
