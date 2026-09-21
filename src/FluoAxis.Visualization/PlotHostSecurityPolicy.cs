namespace FluoAxis.Visualization;

/// <summary>Fail-closed URI policy for the single local PlotHost document.</summary>
public static class PlotHostSecurityPolicy
{
    public const string VirtualHost = "appassets.local";
    public const string DocumentPath = "/plot-host.html";

    public static readonly Uri DocumentUri = new($"https://{VirtualHost}{DocumentPath}");

    public static bool IsTrustedDocument(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri))
        {
            return false;
        }

        return uri.Scheme == Uri.UriSchemeHttps
            && string.Equals(uri.IdnHost, VirtualHost, StringComparison.OrdinalIgnoreCase)
            && uri.IsDefaultPort
            && string.IsNullOrEmpty(uri.UserInfo)
            && string.Equals(uri.AbsolutePath, DocumentPath, StringComparison.Ordinal)
            && string.IsNullOrEmpty(uri.Query)
            && string.IsNullOrEmpty(uri.Fragment);
    }
}
