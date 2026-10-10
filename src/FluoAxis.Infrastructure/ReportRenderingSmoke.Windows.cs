namespace FluoAxis.Infrastructure;

/// <summary>Debug-only native rendering check with caller-supplied artifact root; no machine path is emitted.</summary>
public static class ReportRenderingSmoke
{
    public static async Task RunAsync(string transientArtifactRoot)
    {
        string root = Path.GetFullPath(transientArtifactRoot);
        if (!Path.IsPathFullyQualified(transientArtifactRoot) || !Directory.Exists(root)
            || new DirectoryInfo(root).Attributes.HasFlag(FileAttributes.ReparsePoint))
            throw new InvalidOperationException("SYSTEM_REPORT_SMOKE_ROOT_INVALID");
        string source = Path.Combine(root, "sample.html");
        if (new FileInfo(source).Length > 16 * 1024 * 1024
            || File.GetAttributes(source).HasFlag(FileAttributes.ReparsePoint))
            throw new InvalidOperationException("SYSTEM_REPORT_SMOKE_SOURCE_INVALID");
        try
        {
            string html = await File.ReadAllTextAsync(source);
            byte[] pdf = await PdfReportRenderer.RenderAsync(html, CancellationToken.None);
            await File.WriteAllBytesAsync(Path.Combine(root, "sample.pdf"), pdf);
            await File.WriteAllTextAsync(Path.Combine(root, "native-render-status.txt"), "PASS");
        }
        catch (Exception error)
        {
            string diagnostic = error is InvalidOperationException && error.Message.StartsWith("SYSTEM_REPORT_RENDER_FAILED_", StringComparison.Ordinal)
                ? error.Message : "SYSTEM_NATIVE_REPORT_SMOKE_FAILED";
            await File.WriteAllTextAsync(Path.Combine(root, "native-render-status.txt"), diagnostic);
        }
    }
}
