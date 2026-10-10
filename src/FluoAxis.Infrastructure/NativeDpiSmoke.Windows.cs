namespace FluoAxis.Infrastructure;

using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluoAxis.Domain;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

/// <summary>Debug-only, self-closing native DPI probe; does not change desktop scaling or application settings.</summary>
internal static class NativeDpiSmoke
{
    internal static async Task RunAsync(string relativeOutputDirectory)
    {
        RelativePath relative = RelativePath.Parse(relativeOutputDirectory);
        if (!relative.Value.StartsWith("artifacts/validation/dpi/", StringComparison.Ordinal))
            throw new ArgumentException("DPI evidence must stay under its artifact directory.", nameof(relativeOutputDirectory));
        DirectoryInfo? repository = new(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "FluoAxis.slnx"))) repository = repository.Parent;
        if (repository is null) throw new InvalidOperationException("DPI_PROBE_REPOSITORY_UNAVAILABLE");
        string output = Path.Combine(repository.FullName, relative.Value);
        if (Directory.Exists(output)) throw new InvalidOperationException("DPI_PROBE_OUTPUT_ALREADY_EXISTS");
        // Resolve only within this platform boundary; emit only numeric facts.
        for (DirectoryInfo? parent = new(Path.GetDirectoryName(output)!); parent is not null; parent = parent.Parent)
            if (parent.Exists && parent.Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new InvalidOperationException("DPI_PROBE_OUTPUT_UNSAFE");
        Directory.CreateDirectory(output);
        var completion = new TaskCompletionSource<DpiProbeResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var root = new TextBlock { Text = "FluoAxis · 原生 DPI 检查", Margin = new Thickness(24), FontSize = 16 };
        var window = new Window { Title = "FluoAxis DPI verification", Content = root };
        root.Loaded += (_, _) =>
        {
            nint handle = WinRT.Interop.WindowNative.GetWindowHandle(window);
            nint context = GetWindowDpiAwarenessContext(handle);
            completion.TrySetResult(new DpiProbeResult(AreDpiAwarenessContextsEqual(context, new nint(-4)),
                GetAwarenessFromDpiAwarenessContext(context), GetDpiForWindow(handle), root.XamlRoot.RasterizationScale));
        };
        try
        {
            window.Activate();
            DpiProbeResult result = await completion.Task.WaitAsync(TimeSpan.FromSeconds(20));
            string json = JsonSerializer.Serialize(result, DpiProbeJsonContext.Default.DpiProbeResult);
            await File.WriteAllTextAsync(Path.Combine(output, "window-dpi.json"), json);
        }
        finally { window.Close(); }
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern nint GetWindowDpiAwarenessContext(nint hwnd);

    [DllImport("user32.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AreDpiAwarenessContextsEqual(nint first, nint second);

    [DllImport("user32.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int GetAwarenessFromDpiAwarenessContext(nint value);

    [DllImport("user32.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint GetDpiForWindow(nint hwnd);
}

internal sealed record DpiProbeResult(bool PerMonitorV2, int Awareness, uint WindowDpi, double XamlRasterizationScale);

[JsonSerializable(typeof(DpiProbeResult))]
internal sealed partial class DpiProbeJsonContext : JsonSerializerContext;
