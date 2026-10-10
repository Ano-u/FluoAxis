namespace FluoAxis.Architecture.Tests;

using System.Diagnostics;
using System.Globalization;
using Xunit;

public sealed class WindowsDpiPolicyTests
{
    [Theory]
    [InlineData("PerMonitorV2", "2016", 0)]
    [InlineData("System", "2016", 1)]
    [InlineData("PerMonitorV2", "2005", 1)]
    public async Task DpiGateRejectsSystemScalingAndWrongNamespace(string awareness, string namespaceYear, int expectedExit)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FluoAxis.slnx"))) directory = directory.Parent;
        string repository = directory!.FullName;
        string approved = Path.Combine(repository, "artifacts", "test-tmp");
        string fixture = Path.Combine(approved, nameof(WindowsDpiPolicyTests), Environment.ProcessId.ToString(CultureInfo.InvariantCulture), Guid.NewGuid().ToString("N"));
        string app = Path.Combine(fixture, "src", "FluoAxis.App");
        if (!Path.GetFullPath(fixture).StartsWith(Path.GetFullPath(approved) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unsafe fixture location.");
        Directory.CreateDirectory(app);
        try
        {
            File.WriteAllText(Path.Combine(app, "FluoAxis.App.csproj"), "<Project><PropertyGroup><ApplicationManifest>app.manifest</ApplicationManifest></PropertyGroup></Project>");
            File.WriteAllText(Path.Combine(app, "app.manifest"), $$"""
                <assembly xmlns="urn:schemas-microsoft-com:asm.v1"><application xmlns="urn:schemas-microsoft-com:asm.v3"><windowsSettings>
                <dpiAware xmlns="http://schemas.microsoft.com/SMI/2005/WindowsSettings">true/pm</dpiAware>
                <dpiAwareness xmlns="http://schemas.microsoft.com/SMI/{{namespaceYear}}/WindowsSettings">{{awareness}}</dpiAwareness>
                </windowsSettings></application></assembly>
                """);
            var start = new ProcessStartInfo("pwsh") { WorkingDirectory = app, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            foreach (string argument in new[] { "-NoProfile", "-File", Path.Combine(repository, "build", "Verify-WindowsDpi.ps1"), "-RepositoryRoot", fixture }) start.ArgumentList.Add(argument);
            using Process process = Process.Start(start)!;
            Task<string> stdout = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
            Task<string> stderr = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
            await process.WaitForExitAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
            Assert.Equal(expectedExit, process.ExitCode);
            string output = await stdout + await stderr;
            Assert.Contains("PerMonitorV2", output, StringComparison.Ordinal);
        }
        finally
        {
            if (Path.GetFullPath(fixture).StartsWith(Path.GetFullPath(approved) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                Directory.Delete(fixture, true);
        }
    }
}
