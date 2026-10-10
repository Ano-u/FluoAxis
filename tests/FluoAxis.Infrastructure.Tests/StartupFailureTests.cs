namespace FluoAxis.Infrastructure.Tests;

using FluoAxis.Infrastructure;
using Xunit;

public sealed class StartupFailureTests
{
    [Theory]
    [InlineData("workspace", "SYSTEM_WORKSPACE_OPEN_FAILED")]
    [InlineData("window", "SYSTEM_WINDOW_INITIALIZATION_FAILED")]
    [InlineData("activate", "SYSTEM_WINDOW_ACTIVATION_FAILED")]
    [InlineData("ready", "SYSTEM_WORKBENCH_NOT_READY")]
    [InlineData("arguments", "SYSTEM_STARTUP_ARGUMENT_INVALID")]
    [InlineData("hostile-user-text", "SYSTEM_STARTUP_FAILED")]
    public void StartupStageIsDistinguishedWithoutLeakingExceptionText(string stage, string code)
    {
        var error = new InvalidOperationException("private imported material and external location");
        StartupFailure failure = StartupFailure.FromException(error, stage);
        Assert.Equal(code, failure.Code);
        Assert.DoesNotContain("private", failure.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("hostile", failure.ToString(), StringComparison.Ordinal);
        Assert.Equal(8, failure.HResult.Length);
        Assert.Equal("unclassified", failure.Operation);
    }
}
