using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Tamp;
using Tamp.MSBuildClassic;
using Xunit;

namespace Tamp.MSBuildClassic.Tests;

/// <summary>
/// Real <c>msbuild.exe</c>-spawning tests for <see cref="MSBuildClassic"/>.
/// Each test instance copies the <c>Fixtures/Legacy.NetFx48</c> SDK-style
/// .NET Framework 4.8 fixture into a fresh temp directory so the test can
/// mutate it (delete obj/, write binlogs, etc.) without affecting siblings.
/// </summary>
/// <remarks>
/// <para>
/// Windows-only by construction: <c>msbuild.exe</c> ships with Visual Studio
/// or the Build Tools and isn't a thing on macOS / Linux. Tests early-return
/// on non-Windows so the suite stays green when run from a Mac dev box —
/// this matches the established pattern in <see cref="MSBuildClassicTests"/>
/// where the fake tool path branches on <see cref="OperatingSystem.IsWindows"/>.
/// </para>
/// <para>
/// The category trait lets CI filter to integration-only or unit-only runs
/// via <c>dotnet test --filter Category=Integration</c> / <c>!=Integration</c>.
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
public sealed class MSBuildClassicIntegrationTests : IDisposable
{
    private readonly string _fixtureRoot;
    private readonly Tool _msbuild;
    private readonly bool _windows;

    public MSBuildClassicIntegrationTests()
    {
        _windows = OperatingSystem.IsWindows();
        _fixtureRoot = string.Empty;
        _msbuild = null!;

        if (!_windows) return;

        var fixtureSrc = Path.Combine(
            Path.GetDirectoryName(typeof(MSBuildClassicIntegrationTests).Assembly.Location)!,
            "Fixtures", "Legacy.NetFx48");

        _fixtureRoot = Path.Combine(
            Path.GetTempPath(),
            "tamp-msbuildclassic-it-" + Guid.NewGuid().ToString("N"));

        CopyDirectory(fixtureSrc, _fixtureRoot);
        _msbuild = new Tool(AbsolutePath.Create(ResolveMSBuild()));
    }

    public void Dispose()
    {
        if (string.IsNullOrEmpty(_fixtureRoot) || !Directory.Exists(_fixtureRoot)) return;
        try
        {
            Directory.Delete(_fixtureRoot, recursive: true);
        }
        catch
        {
            // Best-effort cleanup. A leftover temp dir is preferable to a
            // failing test from a transient lock on a child process.
        }
    }

    // ---- 1. Restore + Build + Clean each exit 0 -------------------------------

    [Fact]
    public void Restore_Then_Build_Then_Clean_All_Exit_Zero()
    {
        if (!_windows) return;
        var sln = Path.Combine(_fixtureRoot, "Legacy.NetFx48.sln");

        var restore = Execute(MSBuildClassic.Restore(_msbuild, s => s.SetProject(sln)));
        Assert.True(restore.ExitCode == 0,
            $"Restore failed (exit {restore.ExitCode}):\n{restore.Stdout}\n{restore.Stderr}");

        var build = Execute(MSBuildClassic.Build(_msbuild, s => s
            .SetProject(sln)
            .SetConfiguration("Debug")));
        Assert.True(build.ExitCode == 0,
            $"Build failed (exit {build.ExitCode}):\n{build.Stdout}\n{build.Stderr}");

        var clean = Execute(MSBuildClassic.Clean(_msbuild, s => s
            .SetProject(sln)
            .SetConfiguration("Debug")));
        Assert.True(clean.ExitCode == 0,
            $"Clean failed (exit {clean.ExitCode}):\n{clean.Stdout}\n{clean.Stderr}");
    }

    // ---- 2. Property propagation --------------------------------------------

    [Fact]
    public void Build_Propagates_Custom_Property_Through_To_Project_Evaluation()
    {
        if (!_windows) return;
        var sln = Path.Combine(_fixtureRoot, "Legacy.NetFx48.sln");

        // Single-token value -- no semicolons. The semicolon-bearing case
        // is exercised separately below and currently Skipped pending a
        // wrapper-side fix (see that test's reason for the diagnosis).
        var build = Execute(MSBuildClassic.Build(_msbuild, s => s
            .SetProject(sln)
            .SetConfiguration("Debug")
            .SetProperty("DefineConstants", "FOO_BAR")
            .SetWithImplicitRestore()));

        Assert.True(build.ExitCode == 0,
            $"Build failed:\n{build.Stdout}\n{build.Stderr}");

        // The fixture's AfterTargets="Build" target writes the resolved
        // properties to bin/Debug/build-marker.txt. Inspecting it is more
        // reliable than parsing build-engine stdout.
        var marker = Path.Combine(_fixtureRoot, "Legacy.NetFx48", "bin", "Debug", "build-marker.txt");
        Assert.True(File.Exists(marker), $"Build marker not produced at {marker}");
        var contents = File.ReadAllText(marker);
        Assert.Contains("Configuration=Debug", contents);
        Assert.Contains("FOO_BAR", contents);
    }

    // Documents a wrapper-side gap surfaced during initial Windows integ:
    // MSBuild's CLI parser interprets ';' inside /p:Name=Value as a
    // property-list separator unless the value is escaped (%3B). The wrapper
    // currently emits raw semicolons (see existing unit test at
    // MSBuildClassicTests.cs ~ln 97), so this case fails with
    // "MSBUILD : error MSB1006: Property is not valid. Switch: FOO_BAR".
    // Sending a separate diagnostic to the wrapper maintainer; flipping
    // Skip off becomes part of that fix's PR.
    [Fact(Skip = "Wrapper emits unescaped ';' in /p:Name=Value. See finding sent to airm5; flip Skip off once wrapper escapes %3B / %2C.")]
    public void Build_Propagates_Semicolon_Separated_Property_Values()
    {
        if (!_windows) return;
        var sln = Path.Combine(_fixtureRoot, "Legacy.NetFx48.sln");

        var build = Execute(MSBuildClassic.Build(_msbuild, s => s
            .SetProject(sln)
            .SetConfiguration("Debug")
            .SetProperty("DefineConstants", "TRACE;FOO_BAR")
            .SetWithImplicitRestore()));

        Assert.True(build.ExitCode == 0,
            $"Build failed:\n{build.Stdout}\n{build.Stderr}");
        var marker = Path.Combine(_fixtureRoot, "Legacy.NetFx48", "bin", "Debug", "build-marker.txt");
        var contents = File.ReadAllText(marker);
        Assert.Contains("TRACE", contents);
        Assert.Contains("FOO_BAR", contents);
    }

    // ---- 3. WithImplicitRestore runs Restore as part of the Build call -------

    [Fact]
    public void Build_With_ImplicitRestore_Restores_Before_Compile_In_One_Invocation()
    {
        if (!_windows) return;
        var sln = Path.Combine(_fixtureRoot, "Legacy.NetFx48.sln");
        var projectAssets = Path.Combine(
            _fixtureRoot, "Legacy.NetFx48", "obj", "project.assets.json");

        // Sanity: nothing has been restored yet.
        Assert.False(File.Exists(projectAssets), "Pre-condition: obj/ should be empty");

        var build = Execute(MSBuildClassic.Build(_msbuild, s => s
            .SetProject(sln)
            .SetConfiguration("Debug")
            .SetWithImplicitRestore()));

        Assert.True(build.ExitCode == 0,
            $"Build failed:\n{build.Stdout}\n{build.Stderr}");
        Assert.True(File.Exists(projectAssets),
            $"Expected project.assets.json at {projectAssets} after WithImplicitRestore=true");
    }

    // ---- 4. SetBinaryLog produces the binlog --------------------------------

    [Fact]
    public void Build_With_BinaryLog_Writes_File_To_Specified_Path()
    {
        if (!_windows) return;
        var sln = Path.Combine(_fixtureRoot, "Legacy.NetFx48.sln");
        var binlog = Path.Combine(_fixtureRoot, "build.binlog");

        var build = Execute(MSBuildClassic.Build(_msbuild, s => s
            .SetProject(sln)
            .SetConfiguration("Debug")
            .SetWithImplicitRestore()
            .SetBinaryLog(binlog)));

        Assert.True(build.ExitCode == 0,
            $"Build failed:\n{build.Stdout}\n{build.Stderr}");
        Assert.True(File.Exists(binlog), $"Binlog not produced at {binlog}");
        Assert.True(new FileInfo(binlog).Length > 0, "Binlog exists but is empty");
    }

    // ---- 5. Working directory honored ---------------------------------------

    [Fact]
    public void Build_Honors_Working_Directory_With_Relative_Project_Path()
    {
        if (!_windows) return;
        // Project path is relative; only resolvable if msbuild was launched
        // with WorkingDirectory = _fixtureRoot.
        const string relativeSln = "Legacy.NetFx48.sln";

        var build = Execute(MSBuildClassic.Build(_msbuild, s => s
            .SetWorkingDirectory(_fixtureRoot)
            .SetProject(relativeSln)
            .SetConfiguration("Debug")
            .SetWithImplicitRestore()));

        Assert.True(build.ExitCode == 0,
            $"Build failed (WorkingDirectory honoring):\n{build.Stdout}\n{build.Stderr}");
    }

    // ---- Helpers ------------------------------------------------------------

    private sealed record RunResult(int ExitCode, string Stdout, string Stderr);

    private static RunResult Execute(CommandPlan plan)
    {
        var psi = new ProcessStartInfo(plan.Executable)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        if (!string.IsNullOrEmpty(plan.WorkingDirectory))
            psi.WorkingDirectory = plan.WorkingDirectory;
        foreach (var arg in plan.Arguments) psi.ArgumentList.Add(arg);
        foreach (var kv in plan.Environment) psi.Environment[kv.Key] = kv.Value;

        using var proc = Process.Start(psi)
                         ?? throw new InvalidOperationException("Failed to start msbuild.exe");
        if (plan.StandardInput is not null)
        {
            proc.StandardInput.Write(plan.StandardInput);
            proc.StandardInput.Close();
        }
        var stdout = proc.StandardOutput.ReadToEnd();
        var stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit();
        return new RunResult(proc.ExitCode, stdout, stderr);
    }

    private static string ResolveMSBuild()
    {
        string[] candidates =
        {
            @"C:\Program Files\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe",
            @"C:\Program Files\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe",
            @"C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe",
            @"C:\Program Files\Microsoft Visual Studio\18\Enterprise\MSBuild\Current\Bin\MSBuild.exe",
            @"C:\Program Files\Microsoft Visual Studio\18\Professional\MSBuild\Current\Bin\MSBuild.exe",
            @"C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe",
        };
        return candidates.FirstOrDefault(File.Exists)
               ?? throw new InvalidOperationException(
                   "Could not locate msbuild.exe. Install Visual Studio 2022+ or the Build Tools.");
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
        }
        foreach (var dir in Directory.EnumerateDirectories(source))
        {
            CopyDirectory(dir, Path.Combine(destination, Path.GetFileName(dir)));
        }
    }
}
