using System.Linq;
using Bogus;
using Tamp;
using Tamp.MSBuildClassic;
using Xunit;

namespace Tamp.MSBuildClassic.Tests;

public sealed class MSBuildClassicTests
{
    private static readonly string FakeToolPath = OperatingSystem.IsWindows()
        ? "C:\\fake\\msbuild.exe"
        : "/fake/msbuild";

    private static Tool FakeTool() => new(AbsolutePath.Create(FakeToolPath));

    private static int IndexOf(IReadOnlyList<string> args, string token)
    {
        for (var i = 0; i < args.Count; i++) if (args[i] == token) return i;
        return -1;
    }

    // ---- Project (positional) ----

    [Fact]
    public void Build_Project_Is_First_Argument()
    {
        var plan = MSBuildClassic.Build(FakeTool(), s => s.SetProject("Legacy.sln"));
        Assert.Equal("Legacy.sln", plan.Arguments[0]);
    }

    [Fact]
    public void Build_Without_Project_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => MSBuildClassic.Build(FakeTool(), _ => { }));
        Assert.Contains("Project", ex.Message);
    }

    // ---- Default targets per verb ----

    [Theory]
    [InlineData("Build", "/t:Build")]
    public void Build_Default_Target_Is_Build(string label, string expectedToken)
    {
        _ = label;
        var plan = MSBuildClassic.Build(FakeTool(), s => s.SetProject("a.sln"));
        Assert.Contains(expectedToken, plan.Arguments);
    }

    [Fact]
    public void Restore_Default_Target_Is_Restore()
    {
        var plan = MSBuildClassic.Restore(FakeTool(), s => s.SetProject("a.sln"));
        Assert.Contains("/t:Restore", plan.Arguments);
    }

    [Fact]
    public void Clean_Default_Target_Is_Clean()
    {
        var plan = MSBuildClassic.Clean(FakeTool(), s => s.SetProject("a.sln"));
        Assert.Contains("/t:Clean", plan.Arguments);
    }

    [Fact]
    public void SetTargets_Overrides_Default_And_Joins_With_Semicolons()
    {
        var plan = MSBuildClassic.Build(FakeTool(), s => s
            .SetProject("a.sln")
            .SetTargets("Build", "Pack", "PublishCi"));

        Assert.Contains("/t:Build;Pack;PublishCi", plan.Arguments);
        // Only one /t: argument
        Assert.Single(plan.Arguments, a => a.StartsWith("/t:", StringComparison.Ordinal));
    }

    [Fact]
    public void AddTarget_Appends_To_Target_List()
    {
        var plan = MSBuildClassic.Build(FakeTool(), s => s
            .SetProject("a.sln")
            .SetTargets("Build")
            .AddTarget("Pack"));

        Assert.Contains("/t:Build;Pack", plan.Arguments);
    }

    // ---- /p: properties ----

    [Fact]
    public void Properties_Emit_As_p_Flags()
    {
        var plan = MSBuildClassic.Build(FakeTool(), s => s
            .SetProject("a.sln")
            .SetProperty("DefineConstants", "TRACE;LEGACY")
            .SetProperty("DebugType", "embedded"));

        Assert.Contains("/p:DefineConstants=TRACE;LEGACY", plan.Arguments);
        Assert.Contains("/p:DebugType=embedded", plan.Arguments);
    }

    [Fact]
    public void Configuration_Alias_Emits_p_Configuration()
    {
        var plan = MSBuildClassic.Build(FakeTool(), s => s
            .SetProject("a.sln")
            .SetConfiguration("Release"));

        Assert.Contains("/p:Configuration=Release", plan.Arguments);
    }

    [Fact]
    public void Platform_Alias_Emits_p_Platform_With_Spaces_Preserved()
    {
        var plan = MSBuildClassic.Build(FakeTool(), s => s
            .SetProject("a.sln")
            .SetPlatform("Any CPU"));

        Assert.Contains("/p:Platform=Any CPU", plan.Arguments);
    }

    [Fact]
    public void RestoreProperty_Emits_restoreProperty_Flag()
    {
        var plan = MSBuildClassic.Build(FakeTool(), s => s
            .SetProject("a.sln")
            .SetRestoreProperty("NuGetInteractive", "false")
            .SetRestoreProperty("RestoreLockedMode", "true"));

        Assert.Contains("/restoreProperty:NuGetInteractive=false", plan.Arguments);
        Assert.Contains("/restoreProperty:RestoreLockedMode=true", plan.Arguments);
    }

    // ---- Implicit /restore on Build ----

    [Fact]
    public void Build_Without_WithImplicitRestore_Omits_restore_Switch()
    {
        var plan = MSBuildClassic.Build(FakeTool(), s => s.SetProject("a.sln"));
        Assert.DoesNotContain("/restore", plan.Arguments);
    }

    [Fact]
    public void Build_With_ImplicitRestore_Emits_restore_Switch()
    {
        var plan = MSBuildClassic.Build(FakeTool(), s => s
            .SetProject("a.sln")
            .SetWithImplicitRestore());

        Assert.Contains("/restore", plan.Arguments);
    }

    [Fact]
    public void Restore_Verb_Does_Not_Carry_restore_Switch()
    {
        // /restore is a Build-time switch — on a /t:Restore invocation it would be redundant
        // and confusing. RestoreSettings doesn't expose the WithImplicitRestore knob.
        var plan = MSBuildClassic.Restore(FakeTool(), s => s.SetProject("a.sln"));
        Assert.DoesNotContain("/restore", plan.Arguments);
    }

    // ---- Verbosity ----

    [Theory]
    [InlineData(MSBuildVerbosity.Quiet, "/v:quiet")]
    [InlineData(MSBuildVerbosity.Minimal, "/v:minimal")]
    [InlineData(MSBuildVerbosity.Normal, "/v:normal")]
    [InlineData(MSBuildVerbosity.Detailed, "/v:detailed")]
    [InlineData(MSBuildVerbosity.Diagnostic, "/v:diagnostic")]
    public void Verbosity_Token(MSBuildVerbosity v, string expected)
    {
        var plan = MSBuildClassic.Build(FakeTool(), s => s.SetProject("a.sln").SetVerbosity(v));
        Assert.Contains(expected, plan.Arguments);
    }

    [Fact]
    public void Verbosity_Default_Is_Minimal()
    {
        var plan = MSBuildClassic.Build(FakeTool(), s => s.SetProject("a.sln"));
        Assert.Contains("/v:minimal", plan.Arguments);
    }

    // ---- /m max-cpu ----

    [Fact]
    public void MaxCpuCount_Null_Omits_m_Flag()
    {
        var plan = MSBuildClassic.Build(FakeTool(), s => s.SetProject("a.sln"));
        Assert.DoesNotContain(plan.Arguments, a => a == "/m" || a.StartsWith("/m:", StringComparison.Ordinal));
    }

    [Fact]
    public void MaxCpuCount_Zero_Emits_Bare_m()
    {
        var plan = MSBuildClassic.Build(FakeTool(), s => s.SetProject("a.sln").SetMaxCpuCount(0));
        Assert.Contains("/m", plan.Arguments);
    }

    [Theory]
    [InlineData(1, "/m:1")]
    [InlineData(4, "/m:4")]
    [InlineData(16, "/m:16")]
    public void MaxCpuCount_Positive_Emits_m_N(int n, string expected)
    {
        var plan = MSBuildClassic.Build(FakeTool(), s => s.SetProject("a.sln").SetMaxCpuCount(n));
        Assert.Contains(expected, plan.Arguments);
    }

    // ---- /nologo and /nodeReuse defaults ----

    [Fact]
    public void NoLogo_Default_True_Emits_Flag()
    {
        var plan = MSBuildClassic.Build(FakeTool(), s => s.SetProject("a.sln"));
        Assert.Contains("/nologo", plan.Arguments);
    }

    [Fact]
    public void NoLogo_False_Omits_Flag()
    {
        var plan = MSBuildClassic.Build(FakeTool(), s => s.SetProject("a.sln").SetNoLogo(false));
        Assert.DoesNotContain("/nologo", plan.Arguments);
    }

    [Fact]
    public void DisableNodeReuse_Default_True_Emits_Flag()
    {
        var plan = MSBuildClassic.Build(FakeTool(), s => s.SetProject("a.sln"));
        Assert.Contains("/nodeReuse:false", plan.Arguments);
    }

    [Fact]
    public void DisableNodeReuse_False_Omits_Flag()
    {
        var plan = MSBuildClassic.Build(FakeTool(), s => s.SetProject("a.sln").SetDisableNodeReuse(false));
        Assert.DoesNotContain("/nodeReuse:false", plan.Arguments);
    }

    // ---- Binary log ----

    [Fact]
    public void BinaryLog_Path_Emits_bl_Colon_Path()
    {
        var plan = MSBuildClassic.Build(FakeTool(), s => s
            .SetProject("a.sln")
            .SetBinaryLog("artifacts/build.binlog"));

        Assert.Contains("/bl:artifacts/build.binlog", plan.Arguments);
    }

    [Fact]
    public void BinaryLog_Default_Switch_Emits_Bare_bl()
    {
        var plan = MSBuildClassic.Build(FakeTool(), s => s
            .SetProject("a.sln")
            .SetBinaryLogDefault());

        Assert.Contains("/bl", plan.Arguments);
        // Bare /bl should NOT collide with /bl:path form
        Assert.DoesNotContain(plan.Arguments, a => a.StartsWith("/bl:", StringComparison.Ordinal));
    }

    [Fact]
    public void BinaryLog_Path_Takes_Precedence_Over_Default()
    {
        var plan = MSBuildClassic.Build(FakeTool(), s => s
            .SetProject("a.sln")
            .SetBinaryLog("a.binlog")
            .SetBinaryLogDefault());

        // /bl:path wins; bare /bl is suppressed
        Assert.Contains("/bl:a.binlog", plan.Arguments);
        Assert.DoesNotContain("/bl", plan.Arguments);
    }

    // ---- /ds detailed summary ----

    [Fact]
    public void DetailedSummary_Emits_ds()
    {
        var plan = MSBuildClassic.Build(FakeTool(), s => s
            .SetProject("a.sln")
            .SetDetailedSummary());

        Assert.Contains("/ds", plan.Arguments);
    }

    // ---- Working directory / env vars ----

    [Fact]
    public void WorkingDirectory_Propagates()
    {
        var plan = MSBuildClassic.Build(FakeTool(), s => s
            .SetProject("a.sln")
            .SetWorkingDirectory("/tmp/build"));

        Assert.Equal("/tmp/build", plan.WorkingDirectory);
    }

    [Fact]
    public void EnvironmentVariables_Propagate()
    {
        var plan = MSBuildClassic.Build(FakeTool(), s => s
            .SetProject("a.sln")
            .SetEnvironmentVariable("VisualStudioVersion", "17.0"));

        Assert.True(plan.Environment.TryGetValue("VisualStudioVersion", out var v) && v == "17.0");
    }

    // ---- Executable wiring ----

    [Fact]
    public void Executable_Is_Tool_Path()
    {
        var plan = MSBuildClassic.Build(FakeTool(), s => s.SetProject("a.sln"));
        Assert.Equal(FakeToolPath, plan.Executable);
    }

    // ---- Object-init parity ----

    [Fact]
    public void ObjectInit_Produces_Identical_Args_To_Fluent()
    {
        var fluent = MSBuildClassic.Build(FakeTool(), s => s
            .SetProject("Legacy.sln")
            .SetConfiguration("Release")
            .SetPlatform("Any CPU")
            .SetProperty("DefineConstants", "TRACE")
            .SetWithImplicitRestore()
            .SetVerbosity(MSBuildVerbosity.Detailed)
            .SetMaxCpuCount(4)
            .SetBinaryLog("a.binlog"));

        var settings = new BuildSettings
        {
            Project = "Legacy.sln",
            Configuration = "Release",
            Platform = "Any CPU",
            WithImplicitRestore = true,
            Verbosity = MSBuildVerbosity.Detailed,
            MaxCpuCount = 4,
            BinaryLog = "a.binlog",
        };
        settings.Properties["DefineConstants"] = "TRACE";
        var objInit = MSBuildClassic.Build(FakeTool(), settings);

        Assert.Equal(fluent.Arguments, objInit.Arguments);
    }

    // ---- Composition: a realistic CI build invocation ----

    [Fact]
    public void Realistic_CI_Build_Invocation_Shape()
    {
        // Typical "build a hybrid legacy solution on a CI runner" call.
        var plan = MSBuildClassic.Build(FakeTool(), s => s
            .SetProject("Hybrid.sln")
            .SetConfiguration("Release")
            .SetPlatform("Any CPU")
            .SetWithImplicitRestore()
            .SetMaxCpuCount(0)
            .SetBinaryLog("artifacts/build.binlog")
            .SetProperty("RunCodeAnalysis", "false")
            .SetProperty("TreatWarningsAsErrors", "true"));

        // Project comes first
        Assert.Equal("Hybrid.sln", plan.Arguments[0]);

        // Critical tokens all present
        Assert.Contains("/t:Build", plan.Arguments);
        Assert.Contains("/restore", plan.Arguments);
        Assert.Contains("/p:Configuration=Release", plan.Arguments);
        Assert.Contains("/p:Platform=Any CPU", plan.Arguments);
        Assert.Contains("/p:RunCodeAnalysis=false", plan.Arguments);
        Assert.Contains("/p:TreatWarningsAsErrors=true", plan.Arguments);
        Assert.Contains("/m", plan.Arguments);
        Assert.Contains("/bl:artifacts/build.binlog", plan.Arguments);
        Assert.Contains("/v:minimal", plan.Arguments);
        Assert.Contains("/nologo", plan.Arguments);
        Assert.Contains("/nodeReuse:false", plan.Arguments);
    }

    // ---- Boundary fuzz ----

    [Theory]
    [InlineData("path with spaces/Legacy.sln")]
    [InlineData("artifacts/Δ-π.sln")]
    [InlineData("apps/sub'project/Foo.csproj")]
    public void Project_Path_Roundtrips_Verbatim(string path)
    {
        var plan = MSBuildClassic.Build(FakeTool(), s => s.SetProject(path));
        Assert.Equal(path, plan.Arguments[0]);
    }

    [Fact]
    public void Bulk_Properties_All_Emit()
    {
        var faker = new Faker();
        var pairs = Enumerable.Range(0, 40)
            .Select(_ => (Name: faker.Hacker.Noun() + faker.Random.AlphaNumeric(4), Value: faker.Random.Word()))
            .GroupBy(p => p.Name).Select(g => g.First())
            .ToList();

        var plan = MSBuildClassic.Build(FakeTool(), s =>
        {
            s.SetProject("a.sln");
            foreach (var (n, v) in pairs) s.SetProperty(n, v);
        });

        foreach (var (n, v) in pairs)
        {
            Assert.Contains($"/p:{n}={v}", plan.Arguments);
        }
    }
}
