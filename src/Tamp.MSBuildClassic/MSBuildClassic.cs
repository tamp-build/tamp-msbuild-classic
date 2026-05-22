namespace Tamp.MSBuildClassic;

/// <summary>
/// Typed wrappers for the classic Windows <c>msbuild.exe</c> CLI — the one shipped with
/// Visual Studio / Build Tools that drives .NET Framework projects and legacy MSBuild
/// solutions. Separate from <c>Tamp.DotNet</c> because the CLI uses slash-prefixed flags
/// (<c>/p:Foo=Bar</c>, <c>/t:Target</c>) and the underlying binary is Windows-only.
/// </summary>
/// <remarks>
/// <para>
/// The wrapper itself targets net8/9/10 and just shells out — consumers run Tamp on any
/// supported runtime, and as long as <c>msbuild.exe</c> is resolvable on the build agent
/// (Developer PowerShell / Developer Command Prompt / pre-populated PATH), invocation works.
/// </para>
/// <code>
/// [FromPath("msbuild")] readonly Tool MsBuild = null!;
///
/// Target Compile => _ => _.Executes(() => MSBuildClassic.Build(MsBuild, s => s
///     .SetProject("Legacy.sln")
///     .SetConfiguration("Release")
///     .SetPlatform("Any CPU")
///     .SetProperty("DefineConstants", "TRACE;LEGACY")
///     .SetWithImplicitRestore()));
/// </code>
/// </remarks>
public static class MSBuildClassic
{
    /// <summary><c>msbuild.exe &lt;project&gt; /t:Build</c> (default target list — use <c>SetTargets</c> to override).</summary>
    public static CommandPlan Build(Tool tool, Action<BuildSettings> configure)
        => Run<BuildSettings>(tool, configure);

    /// <summary><c>msbuild.exe &lt;project&gt; /t:Restore</c>.</summary>
    public static CommandPlan Restore(Tool tool, Action<RestoreSettings> configure)
        => Run<RestoreSettings>(tool, configure);

    /// <summary><c>msbuild.exe &lt;project&gt; /t:Clean</c>.</summary>
    public static CommandPlan Clean(Tool tool, Action<CleanSettings> configure)
        => Run<CleanSettings>(tool, configure);

    // ---- Object-init overloads (parallel surface) ----
    public static CommandPlan Build(Tool tool, BuildSettings settings) => Plan(tool, settings);
    public static CommandPlan Restore(Tool tool, RestoreSettings settings) => Plan(tool, settings);
    public static CommandPlan Clean(Tool tool, CleanSettings settings) => Plan(tool, settings);

    private static CommandPlan Run<T>(Tool tool, Action<T>? configure) where T : MSBuildClassicSettingsBase, new()
    {
        if (tool is null) throw new ArgumentNullException(nameof(tool));
        var s = new T();
        configure?.Invoke(s);
        return s.ToCommandPlan(tool);
    }

    private static CommandPlan Plan<T>(Tool tool, T settings) where T : MSBuildClassicSettingsBase
    {
        if (tool is null) throw new ArgumentNullException(nameof(tool));
        if (settings is null) throw new ArgumentNullException(nameof(settings));
        return settings.ToCommandPlan(tool);
    }
}
