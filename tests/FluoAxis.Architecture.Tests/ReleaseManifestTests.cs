namespace FluoAxis.Architecture.Tests;

using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Xunit;

public sealed class ReleaseManifestTests
{
    [Theory]
    [InlineData("valid", true)]
    [InlineData("extra", false)]
    [InlineData("tamper", false)]
    [InlineData("missing", false)]
    [InlineData("duplicate", false)]
    [InlineData("case-collision", false)]
    [InlineData("traversal", false)]
    [InlineData("rooted", false)]
    [InlineData("cache", false)]
    [InlineData("symbols", false)]
    [InlineData("future-schema", false)]
    public async Task GateRequiresExactPortablePayloadAndRejectsRuntimeCache(string scenario, bool expected)
    {
        DirectoryInfo? repository = new(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "FluoAxis.slnx"))) repository = repository.Parent;
        string approved = Path.Combine(repository!.FullName, "artifacts", "test-tmp");
        string root = Path.Combine(approved, nameof(ReleaseManifestTests), Environment.ProcessId.ToString(CultureInfo.InvariantCulture), Guid.NewGuid().ToString("N"));
        if (!Path.GetFullPath(root).StartsWith(Path.GetFullPath(approved) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unsafe fixture root.");
        string payload = Path.Combine(root, "payload");
        Directory.CreateDirectory(payload);
        try
        {
            byte[] bytes = [1, 2, 3];
            string entry = scenario switch
            {
                "traversal" => "../escape.dll", "rooted" => Path.Combine(root, "absolute.dll"),
                "cache" => "FluoAxis.App.exe.WebView2/cache.bin", "symbols" => "symbols.pdb", _ => "FluoAxis.App.exe",
            };
            string path = Path.Combine(payload, scenario is "traversal" or "rooted" ? "FluoAxis.App.exe" : entry);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, bytes);
            var file = new { entry, bytes = bytes.Length, sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)) };
            object[] files = scenario == "duplicate" ? [file, file] : scenario == "case-collision"
                ? [file, new { entry = entry.ToUpperInvariant(), bytes = bytes.Length, file.sha256 }] : [file];
            File.WriteAllText(Path.Combine(payload, "research-build.json"), JsonSerializer.Serialize(new
            { schemaVersion = scenario == "future-schema" ? 2 : 1, product = "FluoAxis", purpose = "research-use-only", files }));
            if (scenario == "extra") File.WriteAllText(Path.Combine(payload, "unexpected.log"), "cache");
            if (scenario == "tamper") File.WriteAllBytes(path, [3, 2, 1]);
            if (scenario == "missing") File.Delete(path);
            var start = new ProcessStartInfo("pwsh") { WorkingDirectory = payload, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            foreach (string argument in new[] { "-NoProfile", "-File", Path.Combine(repository.FullName, "build", "Verify-ReleaseManifest.ps1"), "-RepositoryRoot", root, "-PublishDirectory", "payload" }) start.ArgumentList.Add(argument);
            using Process process = Process.Start(start)!;
            Task<string> stdout = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
            Task<string> stderr = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
            await process.WaitForExitAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
            string output = await stdout + await stderr;
            Assert.True((process.ExitCode == 0) == expected, output);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
