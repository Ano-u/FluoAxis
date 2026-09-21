namespace FluoAxis.Visualization.Tests;

using Xunit;

public sealed class PlotHostSecurityPolicyTests
{
    [Theory]
    [InlineData("https://appassets.local/plot-host.html")]
    [InlineData("https://APPASSETS.LOCAL/plot-host.html")]
    public void TrustedDocumentAcceptsOnlyTheLocalDocument(string value)
    {
        Assert.True(PlotHostSecurityPolicy.IsTrustedDocument(value));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("plot-host.html")]
    [InlineData("http://appassets.local/plot-host.html")]
    [InlineData("https://appassets.local.example/plot-host.html")]
    [InlineData("https://example.appassets.local/plot-host.html")]
    [InlineData("https://appassets.local:444/plot-host.html")]
    [InlineData("https://user@appassets.local/plot-host.html")]
    [InlineData("https://appassets.local/")]
    [InlineData("https://appassets.local/other.html")]
    [InlineData("https://appassets.local/plot-host.html?mode=test")]
    [InlineData("https://appassets.local/plot-host.html#fragment")]
    public void TrustedDocumentRejectsEveryOtherSource(string? value)
    {
        Assert.False(PlotHostSecurityPolicy.IsTrustedDocument(value));
    }
}
