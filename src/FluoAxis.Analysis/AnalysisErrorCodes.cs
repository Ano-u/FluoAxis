namespace FluoAxis.Analysis;

using FluoAxis.Domain;

/// <summary>
/// Additional structured error codes defined by the analysis engine.
/// These complement <see cref="ErrorCodes"/> (Domain) and obey the same
/// code-shape contract enforced by <see cref="AppError.CodePattern"/>.
/// Every code here marks either a QC FAIL diagnosis (carried inside the
/// result objects as <see cref="WellDiagnostic"/> entries, not as failed
/// <see cref="Result{T}"/> values) or a stage precondition violation.
/// </summary>
public static class AnalysisErrorCodes
{
    /// <summary>100% positive plateau relative range exceeded the method maximum (QC FAIL diagnosis).</summary>
    public const string CalibrationPlateauRangeExceeded = "CALIBRATION_PLATEAU_RANGE_EXCEEDED";

    /// <summary>NTC well carries no valid point inside the 0–120 min QC domain (fail closed, Method-Spec §7.7).</summary>
    public const string CalibrationNtcNoValidPoints = "CALIBRATION_NTC_NO_VALID_POINTS";

    /// <summary>Controls or sample analysis was requested before calibration passed (stage precondition).</summary>
    public const string ControlCalibrationNotPassed = "CONTROL_CALIBRATION_NOT_PASSED";

    /// <summary>Sample analysis was requested without the WT reference coordinate (stage precondition).</summary>
    public const string ControlWtReferenceUnavailable = "CONTROL_WT_REFERENCE_UNAVAILABLE";

    /// <summary>LQC replicate wells classified in mixed directions (QC FAIL diagnosis, Method-Spec §9.4 applied to LQC replicates).</summary>
    public const string ControlLqcReplicateInconsistent = "CONTROL_LQC_REPLICATE_INCONSISTENT";
}
