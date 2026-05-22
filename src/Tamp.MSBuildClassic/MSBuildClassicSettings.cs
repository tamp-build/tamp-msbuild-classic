namespace Tamp.MSBuildClassic;

/// <summary>MSBuild verbosity level (<c>/v:</c>).</summary>
public enum MSBuildVerbosity
{
    Quiet,
    Minimal,
    Normal,
    Detailed,
    Diagnostic,
}

/// <summary>
/// Common knobs shared across <c>msbuild.exe</c> verbs. The project / solution path is
/// positional, every other flag is slash-prefixed Windows-classic style.
/// </summary>
/// <remarks>
/// MSBuild ignores duplicate <c>/p:Name=Value</c> flags by keeping the last one — adopters who
/// override a property repeatedly will get the final value. Order in <see cref="CommandPlan.Arguments"/>
/// preserves insertion order on <see cref="Properties"/>.
/// </remarks>
public abstract class MSBuildClassicSettingsBase
{
    /// <summary>Working directory for the spawned <c>msbuild.exe</c> process.</summary>
    public string? WorkingDirectory { get; set; }

    /// <summary>Per-invocation environment variables.</summary>
    public Dictionary<string, string> EnvironmentVariables { get; } = new();

    /// <summary>Project, solution, or .csproj path (positional argument). Required.</summary>
    public string? Project { get; set; }

    /// <summary>Explicit target list (<c>/t:Target1;Target2</c>). Overrides verb default.</summary>
    public List<string> Targets { get; } = new();

    /// <summary>MSBuild properties (<c>/p:Name=Value</c>). Repeatable; last wins per MSBuild semantics.</summary>
    public Dictionary<string, string> Properties { get; } = new();

    /// <summary>Restore-only properties (<c>/restoreProperty:Name=Value</c>).</summary>
    public Dictionary<string, string> RestoreProperties { get; } = new();

    /// <summary>Convenience alias for <c>/p:Configuration=</c>.</summary>
    public string? Configuration { get; set; }

    /// <summary>Convenience alias for <c>/p:Platform=</c> (e.g. <c>"Any CPU"</c>).</summary>
    public string? Platform { get; set; }

    /// <summary>Verbosity (<c>/v:</c>). Default Minimal (matches CI-friendly output).</summary>
    public MSBuildVerbosity Verbosity { get; set; } = MSBuildVerbosity.Minimal;

    /// <summary>
    /// Max CPU count for parallel builds (<c>/m</c> or <c>/m:N</c>).
    /// <c>null</c> omits the flag, <c>0</c> emits <c>/m</c> (auto), positive N emits <c>/m:N</c>.
    /// </summary>
    public int? MaxCpuCount { get; set; }

    /// <summary>Suppress the MSBuild startup banner (<c>/nologo</c>). Default true.</summary>
    public bool NoLogo { get; set; } = true;

    /// <summary>Disable node reuse across invocations (<c>/nodeReuse:false</c>). Default true on CI — prevents stale Node.js / file-handle issues.</summary>
    public bool DisableNodeReuse { get; set; } = true;

    /// <summary>
    /// Binary log path (<c>/bl:&lt;path&gt;</c>). When set but empty, emits bare <c>/bl</c> (writes <c>msbuild.binlog</c> in working dir).
    /// </summary>
    public string? BinaryLog { get; set; }

    /// <summary>Emit <c>/bl</c> with no path (default <c>msbuild.binlog</c>).</summary>
    public bool BinaryLogDefault { get; set; }

    /// <summary>Detailed end-of-build summary (<c>/ds</c>).</summary>
    public bool DetailedSummary { get; set; }

    /// <summary>Verb-specific default targets (subclasses; consumed when <see cref="Targets"/> is empty).</summary>
    protected abstract string DefaultTarget { get; }

    /// <summary>Verb-specific extra arguments (subclasses).</summary>
    protected virtual IEnumerable<string> BuildExtraArguments() => Array.Empty<string>();

    /// <summary>Subclasses extending the secret list.</summary>
    protected virtual IEnumerable<Secret> CollectSecrets() => Array.Empty<Secret>();

    /// <summary>Per-verb validation hook.</summary>
    protected virtual void Validate()
    {
        if (string.IsNullOrEmpty(Project))
            throw new InvalidOperationException("Project is required (set via SetProject).");
    }

    internal CommandPlan ToCommandPlan(Tool tool)
    {
        Validate();

        var args = new List<string>();

        // Positional project FIRST so the rendered command line reads naturally.
        args.Add(Project!);

        // Targets — either explicit list or verb default.
        if (Targets.Count > 0)
            args.Add($"/t:{string.Join(";", Targets)}");
        else if (!string.IsNullOrEmpty(DefaultTarget))
            args.Add($"/t:{DefaultTarget}");

        // Configuration / Platform aliases — emitted BEFORE the general Properties dictionary
        // so an explicit SetProperty("Configuration", ...) call would silently override.
        // (Adopters who want the override should call SetProperty, not SetConfiguration.)
        if (!string.IsNullOrEmpty(Configuration)) args.Add(PropertyArg("Configuration", Configuration!));
        if (!string.IsNullOrEmpty(Platform)) args.Add(PropertyArg("Platform", Platform!));

        // Verb-specific extras (e.g. /restore switch on Build).
        foreach (var a in BuildExtraArguments()) args.Add(a);

        // Properties.
        foreach (var kv in Properties) args.Add(PropertyArg(kv.Key, kv.Value));
        foreach (var kv in RestoreProperties) args.Add($"/restoreProperty:{kv.Key}={EscapePropertyValue(kv.Value)}");

        // Verbosity.
        args.Add($"/v:{VerbosityToken(Verbosity)}");

        if (MaxCpuCount is int m)
            args.Add(m <= 0 ? "/m" : $"/m:{m}");

        if (NoLogo) args.Add("/nologo");
        if (DisableNodeReuse) args.Add("/nodeReuse:false");

        if (!string.IsNullOrEmpty(BinaryLog)) args.Add($"/bl:{BinaryLog}");
        else if (BinaryLogDefault) args.Add("/bl");

        if (DetailedSummary) args.Add("/ds");

        return new CommandPlan
        {
            Executable = tool.Executable.Value,
            Arguments = args,
            Environment = new Dictionary<string, string>(EnvironmentVariables),
            WorkingDirectory = WorkingDirectory ?? tool.WorkingDirectory,
            Secrets = CollectSecrets().ToList(),
        };
    }

    private static string VerbosityToken(MSBuildVerbosity v) => v switch
    {
        MSBuildVerbosity.Quiet => "quiet",
        MSBuildVerbosity.Minimal => "minimal",
        MSBuildVerbosity.Normal => "normal",
        MSBuildVerbosity.Detailed => "detailed",
        MSBuildVerbosity.Diagnostic => "diagnostic",
        _ => throw new InvalidOperationException($"Unknown MSBuildVerbosity: {v}"),
    };

    /// <summary>
    /// MSBuild's CLI parses <c>;</c> and <c>,</c> as property-list separators inside a
    /// <c>/p:Name=Value</c> token. Without escaping, <c>/p:DefineConstants=TRACE;DEBUG</c>
    /// fails with <c>MSB1006: Property is not valid. Switch: DEBUG</c>. URL-encoding both
    /// characters is the canonical workaround.
    /// </summary>
    internal static string EscapePropertyValue(string value)
        => value.Replace(",", "%2C").Replace(";", "%3B");

    /// <summary>Helper for emitting a single <c>/p:Name=Value</c> arg with separator escaping.</summary>
    private static string PropertyArg(string name, string value)
        => $"/p:{name}={EscapePropertyValue(value)}";
}

/// <summary>Fluent setters for the common knobs.</summary>
public static class MSBuildClassicSettingsBaseExtensions
{
    public static T SetWorkingDirectory<T>(this T s, string? cwd) where T : MSBuildClassicSettingsBase { s.WorkingDirectory = cwd; return s; }
    public static T SetProject<T>(this T s, string project) where T : MSBuildClassicSettingsBase { s.Project = project; return s; }
    public static T SetTargets<T>(this T s, params string[] targets) where T : MSBuildClassicSettingsBase { s.Targets.Clear(); s.Targets.AddRange(targets); return s; }
    public static T AddTarget<T>(this T s, string target) where T : MSBuildClassicSettingsBase { s.Targets.Add(target); return s; }
    public static T SetProperty<T>(this T s, string name, string value) where T : MSBuildClassicSettingsBase { s.Properties[name] = value; return s; }
    public static T SetRestoreProperty<T>(this T s, string name, string value) where T : MSBuildClassicSettingsBase { s.RestoreProperties[name] = value; return s; }
    public static T SetConfiguration<T>(this T s, string? config) where T : MSBuildClassicSettingsBase { s.Configuration = config; return s; }
    public static T SetPlatform<T>(this T s, string? platform) where T : MSBuildClassicSettingsBase { s.Platform = platform; return s; }
    public static T SetVerbosity<T>(this T s, MSBuildVerbosity v) where T : MSBuildClassicSettingsBase { s.Verbosity = v; return s; }
    public static T SetMaxCpuCount<T>(this T s, int? n) where T : MSBuildClassicSettingsBase { s.MaxCpuCount = n; return s; }
    public static T SetNoLogo<T>(this T s, bool v = true) where T : MSBuildClassicSettingsBase { s.NoLogo = v; return s; }
    public static T SetDisableNodeReuse<T>(this T s, bool v = true) where T : MSBuildClassicSettingsBase { s.DisableNodeReuse = v; return s; }
    public static T SetBinaryLog<T>(this T s, string? path) where T : MSBuildClassicSettingsBase { s.BinaryLog = path; return s; }
    public static T SetBinaryLogDefault<T>(this T s, bool v = true) where T : MSBuildClassicSettingsBase { s.BinaryLogDefault = v; return s; }
    public static T SetDetailedSummary<T>(this T s, bool v = true) where T : MSBuildClassicSettingsBase { s.DetailedSummary = v; return s; }
    public static T SetEnvironmentVariable<T>(this T s, string name, string value) where T : MSBuildClassicSettingsBase { s.EnvironmentVariables[name] = value; return s; }
}

/// <summary>Settings for <c>msbuild.exe &lt;project&gt; /t:Build</c>.</summary>
public sealed class BuildSettings : MSBuildClassicSettingsBase
{
    /// <summary>
    /// Emit the <c>/restore</c> switch — implicit Restore before Build, in a single invocation.
    /// Distinct from the <c>Restore</c> target: the switch performs an implicit restore against
    /// the project graph BEFORE the build target list runs.
    /// </summary>
    public bool WithImplicitRestore { get; set; }

    public BuildSettings SetWithImplicitRestore(bool v = true) { WithImplicitRestore = v; return this; }

    protected override string DefaultTarget => "Build";

    protected override IEnumerable<string> BuildExtraArguments()
    {
        if (WithImplicitRestore) yield return "/restore";
    }
}

/// <summary>Settings for <c>msbuild.exe &lt;project&gt; /t:Restore</c>.</summary>
public sealed class RestoreSettings : MSBuildClassicSettingsBase
{
    protected override string DefaultTarget => "Restore";
}

/// <summary>Settings for <c>msbuild.exe &lt;project&gt; /t:Clean</c>.</summary>
public sealed class CleanSettings : MSBuildClassicSettingsBase
{
    protected override string DefaultTarget => "Clean";
}
