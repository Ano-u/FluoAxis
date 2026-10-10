namespace FluoAxis.Infrastructure;

using Microsoft.Web.WebView2.Core;

/// <summary>Resolves browser state into writable platform application data, never the installed payload.</summary>
internal static class DesktopWebViewEnvironment
{
    internal static Task<CoreWebView2Environment> CreateAsync()
    {
        string root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string folder = Path.Combine(root, "FluoAxis", "browser-v1");
        Directory.CreateDirectory(folder);
        return CoreWebView2Environment.CreateWithOptionsAsync(null, folder, null).AsTask();
    }
}
