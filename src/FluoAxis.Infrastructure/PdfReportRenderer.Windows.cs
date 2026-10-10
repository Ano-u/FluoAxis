namespace FluoAxis.Infrastructure;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

/// <summary>Local, script-free print renderer; paths and COM exceptions stay inside this boundary.</summary>
internal static class PdfReportRenderer
{
    public static async Task<byte[]> RenderAsync(string html, CancellationToken cancellationToken)
    {
        // NavigateToString has a documented 2 MiB input limit.
        if (System.Text.Encoding.UTF8.GetByteCount(html) > 2 * 1024 * 1024)
            throw new InvalidOperationException("SYSTEM_REPORT_LIMIT_EXCEEDED");
        var webView = new WebView2();
        var window = new Window { Title = "FluoAxis · 科研技术报告预览", Content = webView };
        window.AppWindow.Resize(new Windows.Graphics.SizeInt32(1000, 760));
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lifetime.CancelAfter(TimeSpan.FromSeconds(60));
        window.Closed += (_, _) => lifetime.Cancel();
        string phase = "INITIALIZE";
        try
        {
            window.Activate();
            CoreWebView2Environment environment = await DesktopWebViewEnvironment.CreateAsync().WaitAsync(lifetime.Token);
            await webView.EnsureCoreWebView2Async(environment).AsTask(lifetime.Token);
            CoreWebView2 core = webView.CoreWebView2;
            core.Settings.IsScriptEnabled = false;
            core.Settings.AreHostObjectsAllowed = false;
            core.Settings.IsWebMessageEnabled = false;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.AreBrowserAcceleratorKeysEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsBuiltInErrorPageEnabled = false;
            core.NewWindowRequested += (_, args) => args.Handled = true;
            core.DownloadStarting += (_, args) => { args.Cancel = true; args.Handled = true; };
            core.PermissionRequested += (_, args) => { args.State = CoreWebView2PermissionState.Deny; args.Handled = true; };
            var navigated = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            phase = "NAVIGATE";
            ulong? navigationId = null;
            bool acceptInternalDocument = true;
            core.NavigationStarting += (_, args) =>
            {
                // WinUI's runtime reports NavigateToString as a data document
                // before settling on about:blank. Only the first host-initiated
                // local document is allowed; all later navigation is blocked.
                bool localDocument = args.Uri == "about:blank" || args.Uri.StartsWith("data:text/html", StringComparison.OrdinalIgnoreCase);
                if (!acceptInternalDocument || args.IsUserInitiated || !localDocument)
                {
                    args.Cancel = true;
                    phase = "NAVIGATE_BLOCKED";
                    navigated.TrySetResult(false);
                }
                else { acceptInternalDocument = false; navigationId = args.NavigationId; }
            };
            core.NavigationCompleted += (_, args) =>
            {
                if (navigationId != args.NavigationId) return;
                if (!args.IsSuccess) phase = $"NAVIGATE_{args.WebErrorStatus}";
                navigated.TrySetResult(args.IsSuccess);
            };
            core.NavigateToString(html);
            if (!await navigated.Task.WaitAsync(lifetime.Token)) throw new InvalidOperationException("SYSTEM_REPORT_RENDER_FAILED");
            CoreWebView2PrintSettings settings = core.Environment.CreatePrintSettings();
            settings.ShouldPrintBackgrounds = true;
            settings.ShouldPrintHeaderAndFooter = false;
            settings.Orientation = CoreWebView2PrintOrientation.Landscape;
            settings.PageWidth = 8.2677;
            settings.PageHeight = 11.6929;
            phase = "PRINT";
            using Windows.Storage.Streams.IRandomAccessStream printed = await core.PrintToPdfStreamAsync(settings).AsTask(lifetime.Token);
            using Stream pdf = printed.AsStreamForRead();
            using var output = new MemoryStream();
            phase = "READ";
            byte[] buffer = new byte[64 * 1024];
            while (true)
            {
                int read = await pdf.ReadAsync(buffer, lifetime.Token);
                if (read == 0) break;
                if (output.Length + read > 32 * 1024 * 1024) throw new InvalidOperationException("SYSTEM_REPORT_LIMIT_EXCEEDED");
                await output.WriteAsync(buffer.AsMemory(0, read), lifetime.Token);
            }
            byte[] result = output.ToArray();
            if (result.Length < 5 || !result.AsSpan(0, 5).SequenceEqual("%PDF-"u8)) throw new InvalidOperationException("SYSTEM_REPORT_RENDER_FAILED");
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception error) { throw new InvalidOperationException($"SYSTEM_REPORT_RENDER_FAILED_{phase}_{error.HResult:X8}"); }
        finally
        {
            try { webView.Close(); } catch (Exception) { }
            try { window.Close(); } catch (Exception) { }
        }
    }
}
