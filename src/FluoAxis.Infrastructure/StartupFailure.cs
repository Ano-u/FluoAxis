namespace FluoAxis.Infrastructure;

using System.Diagnostics;
using System.Globalization;

/// <summary>Allowlisted startup diagnostics; exception text, file paths and stack locations never cross this boundary.</summary>
public sealed record StartupFailure(string Code, string Stage, string HResult, string Operation)
{
    public static StartupFailure FromException(Exception exception, string stage)
    {
        ArgumentNullException.ThrowIfNull(exception);
        if (stage is not ("workspace" or "window" or "activate" or "ready" or "arguments")) stage = "unknown";
        string code = stage switch
        {
            "workspace" => "SYSTEM_WORKSPACE_OPEN_FAILED",
            "window" => "SYSTEM_WINDOW_INITIALIZATION_FAILED",
            "activate" => "SYSTEM_WINDOW_ACTIVATION_FAILED",
            "ready" => "SYSTEM_WORKBENCH_NOT_READY",
            "arguments" => "SYSTEM_STARTUP_ARGUMENT_INVALID",
            _ => "SYSTEM_STARTUP_FAILED",
        };
        string[] knownOperations = ["InitializeComponent", "LoadComponent", "RegisterPropertyChangedCallback", "Subscribe",
            "add_ActualThemeChanged", "add_HighContrastChanged", "get_AppWindow", "OnRootSizeChanged", "get_XamlRoot", "UpdateWorkbenchFromViewModel"];
        string operation = "unclassified";
        foreach (StackFrame frame in new StackTrace(exception, false).GetFrames())
        {
            string? method = frame.GetMethod()?.Name;
            if (method is not null && knownOperations.Contains(method, StringComparer.Ordinal)) { operation = method; break; }
        }
        return new StartupFailure(code, stage, exception.HResult.ToString("X8", CultureInfo.InvariantCulture), operation);
    }
}
