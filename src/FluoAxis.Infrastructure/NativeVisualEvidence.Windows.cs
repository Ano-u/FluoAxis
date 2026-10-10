namespace FluoAxis.Infrastructure;

using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Json;
using FluoAxis.Domain;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using Microsoft.Web.WebView2.Core;

/// <summary>Debug-only native XAML rendering evidence; excludes WebView content and emits no machine paths.</summary>
internal static class NativeVisualEvidence
{
    internal static async Task CaptureBrowserAsync(CoreWebView2 browser, string relativeDirectory, string fileName)
    {
        await using var file = new FileStream(ResolveOutput(relativeDirectory, fileName), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        await browser.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, file.AsRandomAccessStream());
    }

    internal static async Task CaptureAsync(UIElement content, string relativeDirectory, string fileName)
    {
        string output = ResolveOutput(relativeDirectory, fileName);
        var bitmap = new RenderTargetBitmap();
        await bitmap.RenderAsync(content);
        IBuffer pixels = await bitmap.GetPixelsAsync();
        if (bitmap.PixelWidth < 1 || bitmap.PixelHeight < 1 || pixels.Length == 0)
            throw new InvalidOperationException("UI_RENDER_EMPTY");
        using var encoded = new InMemoryRandomAccessStream();
        BitmapEncoder encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, encoded);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
            (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, pixels.ToArray());
        await encoder.FlushAsync();
        encoded.Seek(0);
        using Stream read = encoded.AsStreamForRead();
        await using var destination = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.Asynchronous);
        await read.CopyToAsync(destination);
    }

    internal static Task CompleteAsync(string relativeDirectory, string code) =>
        File.WriteAllTextAsync(ResolveOutput(relativeDirectory, "status.txt"), code);

    internal static async Task SaveMeasurementsAsync(string relativeDirectory, Dictionary<string, double> captures, double scale)
    {
        await using var stream = new FileStream(ResolveOutput(relativeDirectory, "measurement.json"), FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous);
        using var writer = new Utf8JsonWriter(stream);
        writer.WriteStartObject();
        writer.WriteString("scenario", "native-xaml-layout-render-and-png-encode");
        writer.WriteString("runtime", Environment.Version.ToString());
        writer.WriteString("os", Environment.OSVersion.Version.ToString());
        writer.WriteNumber("logicalProcessors", Environment.ProcessorCount);
        writer.WriteNumber("rasterizationScale", scale);
        writer.WriteNumber("iterationsPerView", 1);
        writer.WriteBoolean("slaAsserted", false);
        writer.WriteString("scope", "Includes RenderTargetBitmap capture and PNG encoding; excludes settle delay; not interactive frame time.");
        writer.WriteStartObject("captureMilliseconds");
        foreach ((string name, double duration) in captures) writer.WriteNumber(name, duration);
        writer.WriteEndObject();
        writer.WriteEndObject();
        await writer.FlushAsync();
    }

    private static string ResolveOutput(string relativeDirectory, string fileName)
    {
        RelativePath folder = RelativePath.Parse(relativeDirectory);
        RelativePath file = RelativePath.Parse(fileName);
        if (!folder.Value.StartsWith("artifacts/ui-layout/", StringComparison.Ordinal) || file.Value.Contains('/', StringComparison.Ordinal))
            throw new ArgumentException("Visual review output must remain under UI layout artifacts.");
        DirectoryInfo? repository = new(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "FluoAxis.slnx"))) repository = repository.Parent;
        if (repository is null) throw new InvalidOperationException("UI_RENDER_REPOSITORY_UNAVAILABLE");
        string destination = Path.Combine(repository.FullName, folder.Value);
        for (DirectoryInfo? current = new(destination); current is not null; current = current.Parent)
            if (current.Exists && current.Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new InvalidOperationException("UI_RENDER_UNSAFE_ROOT");
        Directory.CreateDirectory(destination);
        return Path.Combine(destination, file.Value);
    }
}
