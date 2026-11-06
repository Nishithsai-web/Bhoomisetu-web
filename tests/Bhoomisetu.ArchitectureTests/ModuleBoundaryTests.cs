using System.Reflection;
using System.Xml.Linq;
using Bhoomisetu.Modules.Identity;
using Bhoomisetu.Modules.Land;
using Bhoomisetu.Modules.Offers;
using Bhoomisetu.SharedKernel;
using Xunit;

namespace Bhoomisetu.ArchitectureTests;

/// <summary>
/// Enforces decision TDR-01: modules may depend only on the shared kernel, never on each other or on the API
/// host, so any module can later be moved out without untangling it.
/// </summary>
public sealed class ModuleBoundaryTests
{
    public static TheoryData<string> ModuleAssemblies() => new()
    {
        typeof(Area).Assembly.GetName().Name!,
        typeof(BestValueRanker).Assembly.GetName().Name!,
        typeof(OtpService).Assembly.GetName().Name!,
    };

    [Theory]
    [MemberData(nameof(ModuleAssemblies))]
    public void Modules_reference_only_the_shared_kernel(string assemblyName)
    {
        var assembly = Assembly.Load(assemblyName);
        var bhoomisetuReferences = assembly.GetReferencedAssemblies()
            .Select(a => a.Name!)
            .Where(name => name.StartsWith("Bhoomisetu.", StringComparison.Ordinal))
            .ToList();

        Assert.All(bhoomisetuReferences, name => Assert.Equal(typeof(IClock).Assembly.GetName().Name, name));
    }

    [Theory]
    [InlineData("Bhoomisetu.Modules.Land")]
    [InlineData("Bhoomisetu.Modules.Offers")]
    [InlineData("Bhoomisetu.Modules.Identity")]
    public void Module_projects_declare_no_package_or_cross_module_references(string project)
    {
        // Assembly references only list what the compiler kept; the project file shows intent too.
        var path = FindRepoFile(Path.Combine("src", project, project + ".csproj"));
        var xml = XDocument.Load(path);

        Assert.Empty(xml.Descendants("PackageReference"));
        var projectRefs = xml.Descendants("ProjectReference").Select(e => (string)e.Attribute("Include")!).ToList();
        Assert.All(projectRefs, include => Assert.EndsWith("Bhoomisetu.SharedKernel.csproj", include, StringComparison.Ordinal));
    }

    [Fact]
    public void Shared_kernel_depends_on_no_other_bhoomisetu_assembly()
    {
        var references = typeof(IClock).Assembly.GetReferencedAssemblies().Select(a => a.Name!);
        Assert.DoesNotContain(references, name => name.StartsWith("Bhoomisetu.", StringComparison.Ordinal));
    }

    [Fact]
    public void Domain_code_never_reads_the_system_clock_directly()
    {
        // Time-dependent rules (OTP expiry, freshness) must use IClock so they can be tested.
        var root = Path.GetDirectoryName(FindRepoFile("Bhoomisetu.slnx"))!;
        var offenders = Directory.GetFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(f => f.Contains($"{Path.DirectorySeparatorChar}Bhoomisetu.Modules.", StringComparison.Ordinal))
            .Where(f => File.ReadAllText(f).Contains("DateTimeOffset.UtcNow", StringComparison.Ordinal) || File.ReadAllText(f).Contains("DateTime.UtcNow", StringComparison.Ordinal))
            .ToList();
        Assert.Empty(offenders);
    }

    private static string FindRepoFile(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relative);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException($"Could not find {relative} above {AppContext.BaseDirectory}.");
    }
}
