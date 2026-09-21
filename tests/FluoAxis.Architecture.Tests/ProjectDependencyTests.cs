namespace FluoAxis.Architecture.Tests;

using System.Diagnostics;
using System.Text.Json;
using System.Xml.Linq;
using Xunit;

public sealed class ProjectDependencyTests
{
    private static readonly IReadOnlyDictionary<string, string[]> AllowedProductionReferences =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["FluoAxis.App"] = ["FluoAxis.PlotProtocol", "FluoAxis.Presentation", "FluoAxis.Visualization"],
            ["FluoAxis.Domain"] = [],
            ["FluoAxis.Analysis"] = ["FluoAxis.Domain"],
            ["FluoAxis.Contracts"] = ["FluoAxis.Domain"],
            ["FluoAxis.Application"] = ["FluoAxis.Analysis", "FluoAxis.Contracts", "FluoAxis.Domain"],
            ["FluoAxis.Import"] = ["FluoAxis.Domain"],
            ["FluoAxis.Infrastructure"] = ["FluoAxis.Application", "FluoAxis.Contracts", "FluoAxis.Domain", "FluoAxis.Import"],
            ["FluoAxis.Persistence"] = ["FluoAxis.Application", "FluoAxis.Domain"],
            ["FluoAxis.PlotProtocol"] = [],
            ["FluoAxis.Presentation"] = ["FluoAxis.Contracts"],
            ["FluoAxis.ProjectFormat"] = ["FluoAxis.Domain"],
            ["FluoAxis.Visualization"] = ["FluoAxis.PlotProtocol"],
        };

    [Fact]
    public void Production_projects_follow_the_approved_dependency_graph()
    {
        string root = FindRepositoryRoot();
        Dictionary<string, string[]> graph = LoadGraph(Path.Combine(root, "src"));

        Assert.Equal(AllowedProductionReferences.Keys.Order(), graph.Keys.Order());
        foreach ((string project, string[] expected) in AllowedProductionReferences)
        {
            Assert.Equal(expected.Order(), graph[project].Order());
        }
    }

    [Fact]
    public void Production_project_graph_is_acyclic()
    {
        string root = FindRepositoryRoot();
        Dictionary<string, string[]> graph = LoadGraph(Path.Combine(root, "src"));
        var visiting = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);

        foreach (string project in graph.Keys)
        {
            Visit(project, graph, visiting, visited);
        }
    }

    [Fact]
    public void Test_projects_reference_only_production_or_test_projects()
    {
        string root = FindRepositoryRoot();
        foreach (string projectFile in Directory.EnumerateFiles(Path.Combine(root, "tests"), "*.csproj", SearchOption.AllDirectories))
        {
            foreach (string reference in ReadReferences(projectFile))
            {
                string resolved = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(projectFile)!, reference));
                Assert.True(IsWithin(Path.Combine(root, "src"), resolved) || IsWithin(Path.Combine(root, "tests"), resolved),
                    $"Project reference escapes approved roots: {Path.GetFileName(projectFile)} -> {reference}");
                Assert.True(File.Exists(resolved), $"Project reference does not resolve to a file: {Path.GetFileName(projectFile)} -> {reference}");
            }
        }
    }

    [Fact]
    public void Path_policy_verifier_accepts_relative_content_from_a_foreign_cwd()
    {
        using PolicyFixture fixture = new();
        fixture.Write("safe.json", "{\"entry\":\"data/raw/source.xlsx\",\"url\":\"https://example.invalid/reference\"}");

        PolicyRun result = fixture.RunVerifier();

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("verification passed", result.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Path_policy_verifier_rejects_forbidden_path_forms()
    {
        using PolicyFixture fixture = new();
        string separator = new((char)92, 1);
        fixture.Write("drive.txt", string.Concat("C", ":", separator, "private", separator, "source.xlsx"));
        fixture.Write("unc.txt", string.Concat(separator, separator, "host", separator, "share"));
        fixture.Write("uri.txt", string.Concat("file", ":", "///", "private/source.xlsx"));
        fixture.Write("posix.txt", string.Concat("\"", "/", "home/research/source.xlsx\""));

        PolicyRun result = fixture.RunVerifier();

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("4 violation(s)", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Path_policy_verifier_rejects_root_variable_extended_unc_and_general_posix_forms()
    {
        using PolicyFixture fixture = new();
        string separator = new((char)92, 1);
        fixture.Write("drive-root.txt", string.Concat("C", ":", separator));
        fixture.Write("drive-variable.ps1", string.Concat("$source = 'C", ":", separator, "$data'"));
        fixture.Write("extended-unc.txt", string.Concat(separator, separator, "?", separator, "UNC", separator, "host", separator, "share"));
        fixture.Write("posix.txt", string.Concat("source=\"", "/", "etc/fluoaxis/config.json", "\""));

        PolicyRun result = fixture.RunVerifier();

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("4 violation(s)", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Path_policy_verifier_scans_web_source_extensions_without_misreading_javascript_regexes()
    {
        using PolicyFixture fixture = new();
        string separator = new((char)92, 1);
        fixture.Write("safe.js", "const whitespace = /\\s+/; const driveLikeRegex = /F:\\/[%p]/;");
        fixture.Write("leak.js", string.Concat("const source = 'C", ":", separator, "private", separator, "source.xlsx';"));
        fixture.Write("leak.html", string.Concat("<span data-source='", separator, separator, "host", separator, "share", "'></span>"));
        fixture.Write("leak.css", string.Concat(".source { content: '", "file", ":///private/source.xlsx'; }"));

        PolicyRun result = fixture.RunVerifier();

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("3 violation(s)", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("safe.js", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Path_policy_verifier_rejects_unc_paths_in_literal_preserving_source_forms()
    {
        using PolicyFixture fixture = new();
        string separator = new((char)92, 1);
        string unc = string.Concat(separator, separator, "host", separator, "share", separator, "source.xlsx");
        fixture.Write("leak.ps1", string.Concat("Get-Content '", unc, "'"));
        fixture.Write("verbatim.cs", string.Concat("string source = @\"", unc, "\";"));
        fixture.Write("raw.cs", string.Concat("string source = \"\"\"", unc, "\"\"\";"));
        fixture.Write("raw.js", string.Concat("const source = String.raw`", unc, "`;"));

        PolicyRun result = fixture.RunVerifier();

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("4 violation(s)", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Path_policy_verifier_rejects_forbidden_paths_in_winui_markup_and_manifests()
    {
        using PolicyFixture fixture = new();
        string separator = new((char)92, 1);
        fixture.Write("drive.xaml", string.Concat("<TextBlock Text='C", ":", separator, "private", separator, "source.xlsx' />"));
        fixture.Write("unc.xaml", string.Concat("<TextBlock Text='", separator, separator, "host", separator, "share' />"));
        fixture.Write("uri.xaml", string.Concat("<TextBlock Text='", "file", ":///private/source.xlsx' />"));
        fixture.Write("app.manifest", string.Concat("<assembly data-source='", separator, separator, "host", separator, "share' />"));

        PolicyRun result = fixture.RunVerifier();

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("4 violation(s)", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Scientific_source_boundary_accepts_result_rendering_generic_plot_math_and_vendor_code()
    {
        using ScientificBoundaryFixture fixture = new();
        fixture.Write("src/FluoAxis.Presentation/ResultViewModel.cs",
            "public sealed record ResultViewModel(string ClassificationLabel, double DrPrimeValue);");
        fixture.Write("web/FluoAxis.PlotHost/plot-host.js",
            "const distance = Math.sqrt((x2 - x1) ** 2 + (y2 - y1) ** 2);");
        fixture.Write("web/FluoAxis.PlotHost/vendor/third-party.js",
            "const drPrime = Math.sqrt((fam - wtFam) ** 2 + (rox - wtRox) ** 2);");

        PolicyRun result = fixture.RunVerifier();

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("verification passed", result.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Scientific_source_boundary_rejects_implementation_dependencies_and_symbols()
    {
        using ScientificBoundaryFixture fixture = new();
        fixture.Write("src/FluoAxis.Presentation/BadViewModel.cs",
            string.Join(Environment.NewLine, "using FluoAxis.Analysis;", "var engine = new BrafAnalysisEngine();"));

        PolicyRun result = fixture.RunVerifier();

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("forbidden implementation namespace", result.Output, StringComparison.Ordinal);
        Assert.Contains("implementation symbol 'BrafAnalysisEngine'", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Scientific_source_boundary_rejects_copied_csharp_formulas()
    {
        using ScientificBoundaryFixture fixture = new();
        fixture.Write("src/FluoAxis.App/BadCalculator.cs", string.Join(Environment.NewLine,
            "var normalized = (rfuValue - fbValue) / (fmValue - fbValue);",
            "var relativeRange = (maxValue - minValue) / meanValue;",
            "var drPrime = Math.Sqrt(dx * dx + dy * dy);",
            "var call = drPrime >= 0.4;"));

        PolicyRun result = fixture.RunVerifier();

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("4 violation(s)", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Scientific_source_boundary_rejects_copied_javascript_formulas()
    {
        using ScientificBoundaryFixture fixture = new();
        fixture.Write("web/FluoAxis.PlotHost/bad-analysis.js", string.Join(Environment.NewLine,
            "const normalized = (signalValue - fbValue) / (fmValue - fbValue);",
            "const relativeRange = (maxValue - minValue) / meanValue;",
            "const drPrime = sqrt(dx ** 2 + dy ** 2);",
            "const positive = drPrime >= methodThreshold;"));

        PolicyRun result = fixture.RunVerifier();

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("4 violation(s)", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Dependency_evidence_is_deterministic_complete_and_cwd_independent()
    {
        string root = FindRepositoryRoot();
        using var fixture = new DependencyEvidenceFixture(root);

        PolicyRun first = fixture.Generate("first");
        PolicyRun second = fixture.Generate("second");

        Assert.Equal(0, first.ExitCode);
        Assert.Equal(0, second.ExitCode);
        Assert.Contains("42 resolved components", first.Output, StringComparison.Ordinal);
        Assert.Equal(fixture.ReadBytes("first", "dependencies-and-licenses.json"), fixture.ReadBytes("second", "dependencies-and-licenses.json"));
        Assert.Equal(fixture.ReadBytes("first", "sbom.cyclonedx.json"), fixture.ReadBytes("second", "sbom.cyclonedx.json"));

        using JsonDocument inventory = fixture.ReadJson("first", "dependencies-and-licenses.json");
        Assert.Equal(42, inventory.RootElement.GetProperty("componentCount").GetInt32());
        Assert.Equal(42, inventory.RootElement.GetProperty("components").GetArrayLength());
        Assert.Equal(5, inventory.RootElement.GetProperty("components").EnumerateArray().Count(component => component.GetProperty("relationship").GetString() == "direct"));

        using JsonDocument bom = fixture.ReadJson("first", "sbom.cyclonedx.json");
        Assert.Equal("CycloneDX", bom.RootElement.GetProperty("bomFormat").GetString());
        Assert.Equal("1.5", bom.RootElement.GetProperty("specVersion").GetString());
        Assert.Equal(42, bom.RootElement.GetProperty("components").GetArrayLength());
        Assert.Equal(43, bom.RootElement.GetProperty("dependencies").GetArrayLength());
        JsonElement rootDependency = bom.RootElement.GetProperty("dependencies")[0];
        Assert.Equal("pkg:generic/FluoAxis", rootDependency.GetProperty("ref").GetString());
        Assert.Equal(5, rootDependency.GetProperty("dependsOn").GetArrayLength());
    }

    [Fact]
    public void Dependency_evidence_fails_closed_when_reviewed_metadata_is_missing()
    {
        string root = FindRepositoryRoot();
        using var fixture = new DependencyEvidenceFixture(root);
        string metadata = File.ReadAllText(Path.Combine(root, "build", "dependency-metadata.json"));
        using JsonDocument source = JsonDocument.Parse(metadata);
        object[] packages = source.RootElement.GetProperty("packages").EnumerateArray()
            .Where(package => package.GetProperty("id").GetString() != "Microsoft.Data.Sqlite")
            .Select(package => JsonSerializer.Deserialize<object>(package.GetRawText())!)
            .ToArray();
        fixture.WriteJson("incomplete-metadata.json", new
        {
            schemaVersion = 1,
            securitySource = "https://github.com/advisories",
            packages,
        });

        PolicyRun result = fixture.Generate("missing", fixture.RelativePath("incomplete-metadata.json"));

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Resolved package has no reviewed metadata: Microsoft.Data.Sqlite", result.Output, StringComparison.Ordinal);
    }

    private static void Visit(string project, IReadOnlyDictionary<string, string[]> graph, HashSet<string> visiting, HashSet<string> visited)
    {
        if (visited.Contains(project))
        {
            return;
        }

        Assert.True(visiting.Add(project), $"Circular project reference detected at {project}.");
        foreach (string dependency in graph[project])
        {
            Assert.True(graph.ContainsKey(dependency), $"Unknown production dependency {project} -> {dependency}.");
            Visit(dependency, graph, visiting, visited);
        }

        visiting.Remove(project);
        visited.Add(project);
    }

    private static Dictionary<string, string[]> LoadGraph(string sourceRoot) =>
        Directory.EnumerateFiles(sourceRoot, "*.csproj", SearchOption.AllDirectories).ToDictionary(
            path => Path.GetFileNameWithoutExtension(path),
            path => ReadReferences(path).Select(reference => Path.GetFileNameWithoutExtension(reference)).ToArray(),
            StringComparer.Ordinal);

    private static string[] ReadReferences(string projectFile) =>
        [.. XDocument.Load(projectFile).Descendants("ProjectReference").Select(element => (string)element.Attribute("Include")!)];

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "FluoAxis.slnx")))
        {
            current = current.Parent;
        }

        return current?.FullName ?? throw new InvalidOperationException("Repository root containing FluoAxis.slnx was not found.");
    }

    private static bool IsWithin(string root, string candidate)
    {
        string normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return candidate.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class PolicyFixture : IDisposable
    {
        private readonly string repositoryRoot = FindRepositoryRoot();
        private readonly string fixtureRoot;

        public PolicyFixture()
        {
            fixtureRoot = Path.Combine(repositoryRoot, "artifacts", "test-tmp", nameof(FluoAxis.Architecture.Tests),
                Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(fixtureRoot);
        }

        public void Write(string relativeName, string content) => File.WriteAllText(Path.Combine(fixtureRoot, relativeName), content);

        public PolicyRun RunVerifier()
        {
            string script = Path.Combine(repositoryRoot, "build", "Verify-RepositoryPolicy.ps1");
            var start = new ProcessStartInfo("pwsh")
            {
                WorkingDirectory = Path.GetDirectoryName(repositoryRoot)!,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            start.ArgumentList.Add("-NoProfile");
            start.ArgumentList.Add("-File");
            start.ArgumentList.Add(script);
            start.ArgumentList.Add("-RepositoryRoot");
            start.ArgumentList.Add(fixtureRoot);

            using Process process = Process.Start(start)!;
            string stdout = process.StandardOutput.ReadToEnd();
            string stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();
            return new PolicyRun(process.ExitCode, stdout + stderr);
        }

        public void Dispose()
        {
            string approvedRoot = Path.GetFullPath(Path.Combine(repositoryRoot, "artifacts", "test-tmp"))
                .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string resolved = Path.GetFullPath(fixtureRoot);
            if (!resolved.StartsWith(approvedRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Refusing to remove a path-policy fixture outside the approved test root.");
            }

            if (Directory.Exists(resolved))
            {
                Directory.Delete(resolved, recursive: true);
            }
        }
    }

    private sealed record PolicyRun(int ExitCode, string Output);

    private sealed class ScientificBoundaryFixture : IDisposable
    {
        private readonly string repositoryRoot = FindRepositoryRoot();
        private readonly string fixtureRoot;

        public ScientificBoundaryFixture()
        {
            fixtureRoot = Path.Combine(repositoryRoot, "artifacts", "test-tmp", nameof(FluoAxis.Architecture.Tests),
                Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(fixtureRoot);
        }

        public void Write(string relativeName, string content)
        {
            string path = Path.Combine(fixtureRoot, relativeName);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }

        public PolicyRun RunVerifier()
        {
            string script = Path.Combine(repositoryRoot, "build", "Verify-ScientificSourceBoundary.ps1");
            var start = new ProcessStartInfo("pwsh")
            {
                WorkingDirectory = Path.GetDirectoryName(repositoryRoot)!,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            start.ArgumentList.Add("-NoProfile");
            start.ArgumentList.Add("-File");
            start.ArgumentList.Add(script);
            start.ArgumentList.Add("-RepositoryRoot");
            start.ArgumentList.Add(fixtureRoot);

            using Process process = Process.Start(start)!;
            string stdout = process.StandardOutput.ReadToEnd();
            string stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();
            return new PolicyRun(process.ExitCode, stdout + stderr);
        }

        public void Dispose()
        {
            string approvedRoot = Path.GetFullPath(Path.Combine(repositoryRoot, "artifacts", "test-tmp"))
                .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string resolved = Path.GetFullPath(fixtureRoot);
            if (!resolved.StartsWith(approvedRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Refusing to remove a scientific-boundary fixture outside the approved test root.");
            }

            if (Directory.Exists(resolved))
            {
                Directory.Delete(resolved, recursive: true);
            }
        }
    }

    private sealed class DependencyEvidenceFixture : IDisposable
    {
        private readonly string repositoryRoot;
        private readonly string fixtureRoot;

        public DependencyEvidenceFixture(string repositoryRoot)
        {
            this.repositoryRoot = repositoryRoot;
            fixtureRoot = Path.Combine(repositoryRoot, "artifacts", "test-tmp", nameof(FluoAxis.Architecture.Tests),
                Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(fixtureRoot);
        }

        public PolicyRun Generate(string name, string? metadataFile = null)
        {
            string script = Path.Combine(repositoryRoot, "build", "Generate-DependencyEvidence.ps1");
            string output = Path.Combine(fixtureRoot, name);
            var start = new ProcessStartInfo("pwsh")
            {
                WorkingDirectory = Path.GetDirectoryName(repositoryRoot)!,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            start.ArgumentList.Add("-NoProfile");
            start.ArgumentList.Add("-File");
            start.ArgumentList.Add(script);
            start.ArgumentList.Add("-RepositoryRoot");
            start.ArgumentList.Add(repositoryRoot);
            start.ArgumentList.Add("-OutputDirectory");
            start.ArgumentList.Add(output);
            if (metadataFile is not null)
            {
                start.ArgumentList.Add("-MetadataFile");
                start.ArgumentList.Add(metadataFile);
            }

            using Process process = Process.Start(start)!;
            string stdout = process.StandardOutput.ReadToEnd();
            string stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();
            return new PolicyRun(process.ExitCode, stdout + stderr);
        }

        public byte[] ReadBytes(string directory, string fileName) => File.ReadAllBytes(Path.Combine(fixtureRoot, directory, fileName));

        public JsonDocument ReadJson(string directory, string fileName) =>
            JsonDocument.Parse(File.ReadAllBytes(Path.Combine(fixtureRoot, directory, fileName)));

        public string RelativePath(string fileName) =>
            Path.GetRelativePath(repositoryRoot, Path.Combine(fixtureRoot, fileName));

        public void WriteJson(string fileName, object value) =>
            File.WriteAllText(Path.Combine(fixtureRoot, fileName), JsonSerializer.Serialize(value));

        public void Dispose()
        {
            string approvedRoot = Path.GetFullPath(Path.Combine(repositoryRoot, "artifacts", "test-tmp"))
                .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string resolved = Path.GetFullPath(fixtureRoot);
            if (!resolved.StartsWith(approvedRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Refusing to remove dependency evidence outside the approved test root.");
            }

            if (Directory.Exists(resolved))
            {
                Directory.Delete(resolved, recursive: true);
            }
        }
    }
}
