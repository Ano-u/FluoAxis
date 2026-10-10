namespace FluoAxis.Infrastructure;

using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluoAxis.Domain;

/// <summary>Bounded, self-closing verification of the actual published application; never deletes or recreates user data.</summary>
internal sealed class DesktopStartupVerification
{
    private readonly string outputDirectory;
    private readonly bool isolated;
    private readonly Stopwatch timer = Stopwatch.StartNew();
    internal bool IsInteractive { get; }

    private DesktopStartupVerification(string outputDirectory, bool isolated, bool interactive = false)
    {
        this.outputDirectory = outputDirectory;
        this.isolated = isolated;
        IsInteractive = interactive;
    }

    internal static DesktopStartupVerification? FromArguments(string[] arguments)
    {
        if (arguments.Length < 2 || arguments[1] is not ("--verify-startup" or "--verify-empty-startup" or "--verify-desktop-interaction")) return null;
        if (arguments.Length != 3) throw new ArgumentException("Startup verification requires exactly one output directory.", nameof(arguments));
        RelativePath entry = RelativePath.Parse(arguments[2]);
        bool interactive = arguments[1] == "--verify-desktop-interaction";
        if (!entry.Value.StartsWith(interactive ? "artifacts/desktop-validation/" : "artifacts/startup-validation/", StringComparison.Ordinal))
            throw new ArgumentException("Startup verification requires its dedicated artifact root.", nameof(arguments));
        DirectoryInfo? repository = new(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "FluoAxis.slnx"))) repository = repository.Parent;
        if (repository is null) throw new InvalidOperationException("STARTUP_VERIFICATION_REPOSITORY_UNAVAILABLE");
        string output = Path.Combine(repository.FullName, entry.Value);
        for (DirectoryInfo? ancestor = new(output); ancestor is not null; ancestor = ancestor.Parent)
            if (ancestor.Exists && ancestor.Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new InvalidOperationException("STARTUP_VERIFICATION_UNSAFE_ROOT");
        if (!interactive && Directory.Exists(output)) throw new InvalidOperationException("STARTUP_VERIFICATION_OUTPUT_EXISTS");
        Directory.CreateDirectory(output);
        return new DesktopStartupVerification(output, interactive || arguments[1] == "--verify-empty-startup", interactive);
    }

    internal DesktopWorkspace OpenWorkspace() => isolated
        ? new DesktopWorkspace(Path.Combine(outputDirectory, "workspace")) : new DesktopWorkspace();

    internal async Task CompleteAsync(string status, string state, StartupFailure? failure = null)
    {
        if (status is not ("pass" or "fail") || state is not ("ready" or "empty" or "error"))
            throw new ArgumentException("Invalid startup verification state.");
        var result = new StartupVerificationResult(status, isolated ? "isolated-empty" : "current-workspace", state,
            timer.Elapsed.TotalMilliseconds, failure, typeof(DesktopWorkspace).Assembly.GetName().Version?.ToString() ?? "unknown");
        await using var stream = new FileStream(Path.Combine(outputDirectory, "startup.json"), FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous);
        await JsonSerializer.SerializeAsync(stream, result, StartupVerificationJsonContext.Default.StartupVerificationResult);
    }
}

internal sealed record StartupVerificationResult(string Status, string Workspace, string State, double ElapsedMilliseconds,
    StartupFailure? Failure, string SoftwareVersion);

[JsonSerializable(typeof(StartupVerificationResult))]
internal sealed partial class StartupVerificationJsonContext : JsonSerializerContext;
