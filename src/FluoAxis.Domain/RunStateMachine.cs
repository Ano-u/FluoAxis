namespace FluoAxis.Domain;

using System.Collections.Frozen;

/// <summary>
/// Explicit transition table of the run lifecycle, mirroring the state
/// diagram of requirements doc §8 exactly. Each failing QC state routes to
/// <see cref="RunState.RunInvalid"/>; <see cref="RunState.Exported"/> and
/// <see cref="RunState.RunInvalid"/> are terminal. Any pair not present in
/// the table is an invalid transition.
/// </summary>
public static class RunStateMachine
{
    private static readonly FrozenDictionary<RunState, FrozenSet<RunState>> Transitions = new Dictionary<RunState, RunState[]>
    {
        [RunState.New] = [RunState.Imported],
        [RunState.Imported] = [RunState.Mapped],
        [RunState.Mapped] = [RunState.Calibrating],
        [RunState.Calibrating] = [RunState.CalibrationPassed, RunState.CalibrationFailed],
        [RunState.CalibrationFailed] = [RunState.RunInvalid],
        [RunState.CalibrationPassed] = [RunState.ControlQc],
        [RunState.ControlQc] = [RunState.ReadyForAnalysis, RunState.ControlQcFailed],
        [RunState.ControlQcFailed] = [RunState.RunInvalid],
        [RunState.ReadyForAnalysis] = [RunState.Analyzing],
        [RunState.Analyzing] = [RunState.Analyzed, RunState.ReplicateConflict],
        [RunState.ReplicateConflict] = [RunState.RunInvalid],
        [RunState.Analyzed] = [RunState.Exported],
        [RunState.Exported] = [],
        [RunState.RunInvalid] = [],
    }.ToFrozenDictionary(kvp => kvp.Key, kvp => kvp.Value.ToFrozenSet());

    public static bool CanTransition(RunState from, RunState to) => Transitions[from].Contains(to);

    /// <summary>States reachable from <paramref name="from"/> in one step.</summary>
    public static IReadOnlySet<RunState> AllowedTransitions(RunState from) => Transitions[from];
}
