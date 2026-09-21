namespace FluoAxis.Domain;

/// <summary>
/// Outcome of a quality-control check or run-level validation.
/// </summary>
public enum QcStatus
{
    Pass,
    Fail,
    Invalid,
}
