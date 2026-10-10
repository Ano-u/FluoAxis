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

    /// <summary>
    /// Returns the deterministic lifecycle path between two states. Public
    /// workflow commands use this path to validate every required edge while
    /// persisting only their terminal state in one atomic mutation.
    /// </summary>
    public static IReadOnlyList<RunState>? FindPath(RunState from, RunState to)
    {
        if (!Enum.IsDefined(from) || !Enum.IsDefined(to))
        {
            return null;
        }

        if (from == to)
        {
            return [from];
        }

        Queue<RunState> pending = new();
        Dictionary<RunState, RunState?> previous = [];
        pending.Enqueue(from);
        previous[from] = null;

        while (pending.Count > 0)
        {
            RunState current = pending.Dequeue();
            foreach (RunState next in Transitions[current].Order())
            {
                if (previous.ContainsKey(next))
                {
                    continue;
                }

                previous[next] = current;
                if (next == to)
                {
                    List<RunState> path = [next];
                    RunState cursor = current;
                    while (true)
                    {
                        path.Add(cursor);
                        if (previous[cursor] is not RunState parent)
                        {
                            break;
                        }

                        cursor = parent;
                    }

                    path.Reverse();
                    return path;
                }

                pending.Enqueue(next);
            }
        }

        return null;
    }

    /// <summary>Checks that a supplied path is a contiguous legal lifecycle path.</summary>
    public static bool IsValidPath(IReadOnlyList<RunState> path, RunState from, RunState to)
    {
        if (path is null || path.Count < 2 || path[0] != from || path[^1] != to)
        {
            return false;
        }

        for (int index = 1; index < path.Count; index++)
        {
            if (!CanTransition(path[index - 1], path[index]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>States reachable from <paramref name="from"/> in one step.</summary>
    public static IReadOnlySet<RunState> AllowedTransitions(RunState from) => Transitions[from];
}
