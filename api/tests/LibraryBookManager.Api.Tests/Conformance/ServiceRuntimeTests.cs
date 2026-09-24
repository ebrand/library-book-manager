using System.Text.RegularExpressions;
using System.Xml.Linq;
using LibraryBookManager.Api.Tests.Infrastructure;

namespace LibraryBookManager.Api.Tests.Conformance;

/// <summary>
/// plt-svc-001 — the repository state the platform conformance check inspects. The check
/// itself is not available here (see TRACE.md); these tests hold the repository to it.
/// </summary>
public sealed partial class ServiceRuntimeTests
{
    private static IEnumerable<string> ProjectFiles() =>
        Directory.GetFiles(RepoPaths.ApiRoot, "*.csproj", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"));

    private static string TargetFramework(string projectFile)
    {
        // A project may inherit its framework from Directory.Build.props.
        var own = XDocument.Load(projectFile).Descendants("TargetFramework").Select(e => e.Value).FirstOrDefault();
        return own ?? XDocument.Load(Path.Combine(RepoPaths.ApiRoot, "Directory.Build.props"))
            .Descendants("TargetFramework").Single().Value;
    }

    [Fact(DisplayName = "REQ-SVC-001: every project targets .NET 8 or a later LTS release")]
    public void Targets_supported_lts()
    {
        var projects = ProjectFiles().ToList();
        Assert.NotEmpty(projects);
        foreach (var project in projects)
        {
            var framework = TargetFramework(project);
            var match = NetVersion().Match(framework);
            Assert.True(match.Success, $"{project}: {framework}");
            var major = int.Parse(match.Groups[1].Value);
            Assert.True(major >= 8 && major % 2 == 0, $"{project} targets {framework}, not .NET 8 or a later LTS");
        }
    }

    [Fact(DisplayName = "REQ-SVC-002: projects target the newest LTS (.NET 10, GA November 2025), not the previous one")]
    public void Targets_newest_lts()
    {
        Assert.All(ProjectFiles(), project => Assert.Equal("net10.0", TargetFramework(project)));
    }

    [Fact(DisplayName = "REQ-SVC-003: the service is packaged by a Dockerfile building an OCI image")]
    public void Has_dockerfile()
    {
        var dockerfile = Path.Combine(RepoPaths.ApiRoot, "Dockerfile");
        Assert.True(File.Exists(dockerfile));
        Assert.Contains(File.ReadAllLines(dockerfile), l => l.TrimStart().StartsWith("FROM ", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "REQ-SVC-004: every project commits packages.lock.json and restore is locked in CI and in the image build")]
    public void Lock_files_committed()
    {
        foreach (var project in ProjectFiles())
            Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(project)!, "packages.lock.json")), project);

        var props = File.ReadAllText(Path.Combine(RepoPaths.ApiRoot, "Directory.Build.props"));
        Assert.Contains("<RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>", props);
        Assert.Contains("--locked-mode", File.ReadAllText(Path.Combine(RepoPaths.ApiRoot, "Dockerfile")));
        foreach (var project in ProjectFiles())
            Assert.DoesNotMatch(FloatingVersion(), File.ReadAllText(project));
    }

    [GeneratedRegex(@"^net(\d+)\.0$")]
    private static partial Regex NetVersion();

    [GeneratedRegex(@"Version=""[^""]*[\*\[\(]")]
    private static partial Regex FloatingVersion();
}
