namespace FluoAxis.Architecture.Tests;

using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Xunit;

/// <summary>Runtime-resource shape is required independently of a self-consistent file hash manifest.</summary>
public sealed class PublishedAssetPolicyTests
{
    private static readonly string[] RequiredEntries =
    [
        "App.xbf", "MainWindow.xbf", "Themes/WorkbenchTheme.xbf",
        "FluoAxis.App.pri", "FluoAxis.Visualization.pri",
        "PlotHost/plot-host.html", "PlotHost/plot-host.css", "PlotHost/plot-host.js",
        "PlotHost/vendor/plotly.min.js", "PlotHost/vendor/plotly.LICENSE.txt",
    ];

    public static TheoryData<string> MissingAssets => new(RequiredEntries);

    [Fact]
    public async Task Complete_resource_shapes_pass_from_a_foreign_working_directory()
    {
        using var fixture = new Fixture();

        GateResult result = await fixture.VerifyAsync();

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Contains("passed", result.Output, StringComparison.OrdinalIgnoreCase);
        // These are only format-shaped fixtures. A green asset gate does not
        // assert WinUI can load them; actual published startup is another gate.
    }

    [Theory]
    [MemberData(nameof(MissingAssets))]
    public async Task Missing_required_resource_fails_even_with_valid_hashes_for_remaining_files(string missingEntry)
    {
        using var fixture = new Fixture();
        fixture.Delete(missingEntry);
        fixture.WriteHashManifest();

        GateResult result = await fixture.VerifyAsync();

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains(missingEntry, result.Output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("App.xbf", "magic")]
    [InlineData("MainWindow.xbf", "length")]
    [InlineData("Themes/WorkbenchTheme.xbf", "truncated")]
    [InlineData("FluoAxis.App.pri", "magic")]
    [InlineData("FluoAxis.Visualization.pri", "length")]
    [InlineData("FluoAxis.App.pri", "truncated")]
    public async Task Malformed_binary_resource_fails_despite_a_matching_file_hash(string entry, string corruption)
    {
        using var fixture = new Fixture();
        fixture.Corrupt(entry, corruption);
        fixture.WriteHashManifest();

        GateResult result = await fixture.VerifyAsync();

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains(entry, result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Empty_plot_asset_is_not_a_complete_release()
    {
        using var fixture = new Fixture();
        fixture.Write("PlotHost/plot-host.js", []);
        fixture.WriteHashManifest();

        GateResult result = await fixture.VerifyAsync();

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("PlotHost/plot-host.js", result.Output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("parent")]
    [InlineData("rooted")]
    public async Task Publish_directory_must_be_relative_and_cannot_escape_the_requested_repository(string kind)
    {
        using var fixture = new Fixture();
        string directory = kind == "rooted" ? fixture.PublishRoot : "../outside";

        GateResult result = await fixture.VerifyAsync(directory);

        Assert.NotEqual(0, result.ExitCode);
    }

    private sealed class Fixture : IDisposable
    {
        private const string PublishEntry = "artifacts/release/research";
        private readonly string repository;
        private readonly string approved;
        private readonly string fixtureRoot;
        public string PublishRoot => Path.Combine(fixtureRoot, PublishEntry);

        internal Fixture()
        {
            DirectoryInfo? root = new(AppContext.BaseDirectory);
            while (root is not null && !File.Exists(Path.Combine(root.FullName, "FluoAxis.slnx"))) root = root.Parent;
            repository = root?.FullName ?? throw new InvalidOperationException("Repository root unavailable.");
            approved = Path.Combine(repository, "artifacts", "test-tmp");
            fixtureRoot = Path.Combine(approved, nameof(PublishedAssetPolicyTests),
                Environment.ProcessId.ToString(CultureInfo.InvariantCulture), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(fixtureRoot, "foreign-cwd"));
            foreach (string entry in RequiredEntries)
                Write(entry, entry.EndsWith(".xbf", StringComparison.Ordinal) ? XbfShape()
                    : entry.EndsWith(".pri", StringComparison.Ordinal) ? PriShape() : Encoding.UTF8.GetBytes("synthetic resource\n"));
            WriteHashManifest();
        }

        internal void Write(string entry, byte[] bytes)
        {
            string target = Path.Combine(PublishRoot, entry);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllBytes(target, bytes);
        }

        internal void Delete(string entry)
        {
            string target = Path.GetFullPath(Path.Combine(PublishRoot, entry));
            if (!target.StartsWith(Path.GetFullPath(PublishRoot) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Unsafe test asset deletion.");
            File.Delete(target);
        }

        internal void Corrupt(string entry, string corruption)
        {
            byte[] bytes = File.ReadAllBytes(Path.Combine(PublishRoot, entry));
            if (corruption == "magic") bytes[0] = 0;
            else if (corruption == "length") BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(entry.EndsWith(".xbf", StringComparison.Ordinal) ? 4 : 12), 1);
            else if (corruption == "truncated") bytes = bytes[..8];
            else throw new ArgumentException("Unknown resource corruption.", nameof(corruption));
            Write(entry, bytes);
        }

        internal void WriteHashManifest()
        {
            var files = RequiredEntries.Where(entry => File.Exists(Path.Combine(PublishRoot, entry))).Select(entry =>
            {
                byte[] content = File.ReadAllBytes(Path.Combine(PublishRoot, entry));
                return new { entry, bytes = content.Length, sha256 = Convert.ToHexStringLower(SHA256.HashData(content)) };
            }).ToArray();
            Write("research-build.json", JsonSerializer.SerializeToUtf8Bytes(new { schemaVersion = 1, files }));
        }

        internal async Task<GateResult> VerifyAsync(string? publishDirectory = null)
        {
            var start = new ProcessStartInfo("pwsh")
            {
                WorkingDirectory = Path.Combine(fixtureRoot, "foreign-cwd"), UseShellExecute = false,
                RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true,
            };
            foreach (string argument in new[] { "-NoProfile", "-File", Path.Combine(repository, "build", "Verify-PublishedAssets.ps1"),
                "-RepositoryRoot", fixtureRoot, "-PublishDirectory", publishDirectory ?? PublishEntry }) start.ArgumentList.Add(argument);
            using Process process = Process.Start(start) ?? throw new InvalidOperationException("Asset verifier did not start.");
            CancellationToken token = TestContext.Current.CancellationToken;
            Task<string> stdout = process.StandardOutput.ReadToEndAsync(token);
            Task<string> stderr = process.StandardError.ReadToEndAsync(token);
            try { await process.WaitForExitAsync(token).WaitAsync(TimeSpan.FromSeconds(30), token); }
            catch
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                throw;
            }
            return new(process.ExitCode, await stdout + await stderr);
        }

        private static byte[] XbfShape()
        {
            byte[] bytes = new byte[20];
            "XBF\0"u8.CopyTo(bytes);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), 4);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), 4);
            return bytes;
        }

        private static byte[] PriShape()
        {
            byte[] bytes = new byte[32];
            "mrm_pri2"u8.CopyTo(bytes);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), (uint)bytes.Length);
            return bytes;
        }

        public void Dispose()
        {
            string target = Path.GetFullPath(fixtureRoot);
            if (!target.StartsWith(Path.GetFullPath(approved) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Unsafe test fixture cleanup.");
            if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
        }
    }

    private sealed record GateResult(int ExitCode, string Output);
}
