# Tamp.MSBuildClassic

> Typed wrappers for the classic Windows `msbuild.exe` CLI — Build / Restore / Clean targeting .NET Framework and legacy MSBuild projects. Separate from `Tamp.DotNet` because the CLI uses slash-prefixed flags (`/p:Foo=Bar`, `/t:Target`) and the underlying binary is Windows-only.

| Package | Status |
|---|---|
| `Tamp.MSBuildClassic` | 0.1.0 (initial) |

## Install

```bash
dotnet add package Tamp.MSBuildClassic
```

Multi-targets net8 / net9 / net10. The **wrapper assembly** runs on any supported runtime — but `msbuild.exe` itself is Windows-only, so build agents that invoke it must be Windows.

## When to use this vs. `Tamp.DotNet`

| Want to drive | Use |
|---|---|
| `dotnet build`, `dotnet test`, `dotnet pack` (SDK-style projects, .NET 6+) | `Tamp.DotNet` |
| `msbuild.exe Legacy.sln /p:Configuration=Release` (.NET Framework, classic MSBuild) | `Tamp.MSBuildClassic` |
| Hybrid solutions with both flavors of project | Both — use each for the projects that fit |

## Quick start

```csharp
using Tamp;
using Tamp.MSBuildClassic;

class Build : TampBuild
{
    public static int Main(string[] args) => Execute<Build>(args);

    [FromPath("msbuild")] readonly Tool MsBuild = null!;

    Target Compile => _ => _.Executes(() => MSBuildClassic.Build(MsBuild, s => s
        .SetProject("Legacy.sln")
        .SetConfiguration("Release")
        .SetPlatform("Any CPU")
        .SetWithImplicitRestore()
        .SetMaxCpuCount(0)
        .SetBinaryLog("artifacts/build.binlog")));
}
```

## Verb surface (v1)

| Verb | Default target | Notes |
|---|---|---|
| `Build` | `/t:Build` | Also exposes `WithImplicitRestore` to emit the `/restore` switch (single-invocation Restore + Build). |
| `Restore` | `/t:Restore` | Restore target — distinct from the `/restore` switch on Build. |
| `Clean` | `/t:Clean` | Delete intermediate and final outputs. |

Override the implicit target list any time with `.SetTargets("Build", "Pack", "PublishCi")`.

## Common knobs

- **Properties** — `/p:Name=Value`, repeatable via `.SetProperty(name, value)`. Last writer wins per MSBuild semantics.
- **Restore properties** — `/restoreProperty:Name=Value` via `.SetRestoreProperty(name, value)`.
- **Configuration / Platform aliases** — sugar for the two properties you set every time. Spaces in `Platform` (e.g. `"Any CPU"`) preserved verbatim.
- **Verbosity** — `/v:` with the standard 5-level enum (`Quiet` … `Diagnostic`). Default is `Minimal` (CI-friendly).
- **MaxCpuCount** — `null` omits the flag, `0` emits bare `/m`, positive `N` emits `/m:N`.
- **BinaryLog** — `.SetBinaryLog("artifacts/x.binlog")` for an explicit path, or `.SetBinaryLogDefault()` for bare `/bl` (writes `msbuild.binlog` in the working dir). Path form wins if both are set.
- **NoLogo** — defaults true (CI-friendly).
- **DisableNodeReuse** — defaults true (`/nodeReuse:false`). Prevents stale node carryover between invocations on persistent build agents.

## Settings authoring — fluent or object-init

Both styles produce identical `CommandPlan`s:

```csharp
// Fluent
MSBuildClassic.Build(MsBuild, s => s
    .SetProject("Legacy.sln")
    .SetConfiguration("Release")
    .SetWithImplicitRestore());

// Object-init
MSBuildClassic.Build(MsBuild, new BuildSettings
{
    Project = "Legacy.sln",
    Configuration = "Release",
    WithImplicitRestore = true,
});
```

## Resolving `msbuild.exe`

The wrapper accepts a `Tool` — it does not locate `msbuild.exe` for you. Typical patterns:

- **Developer PowerShell / Developer Command Prompt** — `msbuild` is on `PATH` after VS or Build Tools installs and you've shelled into the Dev environment. `[FromPath("msbuild")]` works.
- **Hand-coded path** — point the `Tool` at an absolute path, e.g. `C:\Program Files\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe`.
- **vswhere-based discovery** — a separate helper for `vswhere` -driven resolution is on the roadmap (not v1).

## License

MIT — see [LICENSE](LICENSE).
