namespace FluoAxis.Domain;

/// <summary>
/// Severity of an <see cref="AppError"/>. Blocking and Fatal prevent
/// continuation of the official guided workflow.
/// </summary>
public enum ErrorSeverity
{
    Info,
    Warning,
    Blocking,
    Fatal,
}
