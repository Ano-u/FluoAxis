namespace FluoAxis.Visualization;

using FluoAxis.PlotProtocol;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

/// <summary>Owns the security boundary between trusted C# and the local PlotHost.</summary>
public sealed class PlotHostWebViewAdapter : IDisposable
{
    private readonly WebView2 webView;
    private bool initialized;
    private bool disposed;

    public PlotHostWebViewAdapter(WebView2 webView)
    {
        this.webView = webView ?? throw new ArgumentNullException(nameof(webView));
    }

    public event EventHandler<TrustedPlotMessageEventArgs>? TrustedMessageReceived;

    public async Task InitializeAsync()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (initialized)
        {
            return;
        }

        try
        {
            await webView.EnsureCoreWebView2Async();
        }
        catch (Exception error) when (error is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            throw new PlotHostInitializationException("PLOT_RUNTIME_UNAVAILABLE", error);
        }
        CoreWebView2 core = webView.CoreWebView2;
        string assetFolder = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "PlotHost"));
        if (!Directory.Exists(assetFolder))
        {
            throw new PlotHostInitializationException("PLOT_HOST_ASSETS_UNAVAILABLE");
        }

        core.SetVirtualHostNameToFolderMapping(
            PlotHostSecurityPolicy.VirtualHost,
            assetFolder,
            CoreWebView2HostResourceAccessKind.DenyCors);
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.AreBrowserAcceleratorKeysEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsZoomControlEnabled = false;
        core.Settings.AreHostObjectsAllowed = false;

        core.NavigationStarting += OnNavigationStarting;
        core.NewWindowRequested += OnNewWindowRequested;
        core.DownloadStarting += OnDownloadStarting;
        core.PermissionRequested += OnPermissionRequested;
        core.WebMessageReceived += OnWebMessageReceived;
        webView.Source = PlotHostSecurityPolicy.DocumentUri;
        initialized = true;
    }

    public void PostJson(string json)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        if (!initialized)
        {
            throw new InvalidOperationException("The PlotHost has not been initialized.");
        }

        byte[] utf8 = System.Text.Encoding.UTF8.GetBytes(json);
        PlotParseResult parsed = PlotMessageCodec.Parse(utf8, PlotMessageDirection.HostToScript);
        if (!parsed.IsSuccess)
        {
            throw new ArgumentException($"The outbound plot message is invalid: {parsed.Error}.", nameof(json));
        }

        webView.CoreWebView2.PostWebMessageAsJson(json);
    }

    private static void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs args)
    {
        if (!PlotHostSecurityPolicy.IsTrustedDocument(args.Uri))
        {
            args.Cancel = true;
        }
    }

    private static void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs args)
    {
        args.Handled = true;
    }

    private static void OnDownloadStarting(object? sender, CoreWebView2DownloadStartingEventArgs args)
    {
        args.Cancel = true;
        args.Handled = true;
    }

    private static void OnPermissionRequested(object? sender, CoreWebView2PermissionRequestedEventArgs args)
    {
        args.State = CoreWebView2PermissionState.Deny;
        args.Handled = true;
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        if (!PlotHostSecurityPolicy.IsTrustedDocument(args.Source))
        {
            return;
        }

        string json;
        try
        {
            json = args.WebMessageAsJson;
        }
        catch (InvalidOperationException)
        {
            return;
        }

        byte[] utf8 = System.Text.Encoding.UTF8.GetBytes(json);
        PlotParseResult parsed = PlotMessageCodec.Parse(utf8, PlotMessageDirection.ScriptToHost);
        if (parsed.IsSuccess)
        {
            TrustedMessageReceived?.Invoke(this, new TrustedPlotMessageEventArgs(parsed.Message!));
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        if (initialized)
        {
            CoreWebView2 core = webView.CoreWebView2;
            core.NavigationStarting -= OnNavigationStarting;
            core.NewWindowRequested -= OnNewWindowRequested;
            core.DownloadStarting -= OnDownloadStarting;
            core.PermissionRequested -= OnPermissionRequested;
            core.WebMessageReceived -= OnWebMessageReceived;
        }
    }
}

public sealed class TrustedPlotMessageEventArgs : EventArgs
{
    public TrustedPlotMessageEventArgs(ParsedPlotMessage message)
    {
        Message = message ?? throw new ArgumentNullException(nameof(message));
    }

    public ParsedPlotMessage Message { get; }
}

public sealed class PlotHostInitializationException : Exception
{
    public PlotHostInitializationException(string errorCode) : this(errorCode, null)
    {
    }

    public PlotHostInitializationException(string errorCode, Exception? innerException)
        : base(errorCode, innerException)
    {
        ErrorCode = errorCode;
    }

    public string ErrorCode { get; }
}
