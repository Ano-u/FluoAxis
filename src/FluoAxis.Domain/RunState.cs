namespace FluoAxis.Domain;

/// <summary>
/// Lifecycle state of one guided-analysis run. Transitions are governed by
/// <see cref="RunStateMachine"/>; UI navigation never changes these states.
/// </summary>
public enum RunState
{
    New,
    Imported,
    Mapped,
    Calibrating,
    CalibrationFailed,
    CalibrationPassed,
    ControlQc,
    ControlQcFailed,
    ReadyForAnalysis,
    Analyzing,
    Analyzed,
    ReplicateConflict,
    Exported,
    RunInvalid,
}
