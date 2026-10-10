namespace FluoAxis.Domain.Tests;

using Xunit;

public class RunStateMachineTests
{
    private static readonly (RunState From, RunState To)[] LegalTransitions =
    [
        (RunState.New, RunState.Imported),
        (RunState.Imported, RunState.Mapped),
        (RunState.Mapped, RunState.Calibrating),
        (RunState.Calibrating, RunState.CalibrationPassed),
        (RunState.Calibrating, RunState.CalibrationFailed),
        (RunState.CalibrationFailed, RunState.RunInvalid),
        (RunState.CalibrationPassed, RunState.ControlQc),
        (RunState.ControlQc, RunState.ReadyForAnalysis),
        (RunState.ControlQc, RunState.ControlQcFailed),
        (RunState.ControlQcFailed, RunState.RunInvalid),
        (RunState.ReadyForAnalysis, RunState.Analyzing),
        (RunState.Analyzing, RunState.Analyzed),
        (RunState.Analyzing, RunState.ReplicateConflict),
        (RunState.ReplicateConflict, RunState.RunInvalid),
        (RunState.Analyzed, RunState.Exported),
    ];

    private static readonly RunState[] TerminalStates = [RunState.Exported, RunState.RunInvalid];

    private static IEnumerable<RunState> AllStates() => Enum.GetValues<RunState>();

    [Theory]
    [MemberData(nameof(LegalPairs))]
    public void Legal_transitions_return_true(RunState from, RunState to)
    {
        Assert.True(RunStateMachine.CanTransition(from, to));
    }

    [Fact]
    public void Every_documented_legal_transition_is_in_the_table()
    {
        var table = LegalTransitions.ToHashSet();
        var enumerated = new HashSet<(RunState, RunState)>();

        foreach (RunState from in AllStates())
        {
            foreach (RunState to in RunStateMachine.AllowedTransitions(from))
            {
                enumerated.Add((from, to));
            }
        }

        Assert.Equal(table, enumerated);
    }

    [Fact]
    public void Illegal_transitions_return_false_over_the_full_matrix()
    {
        var legal = LegalTransitions.ToHashSet();
        var failures = new List<string>();

        foreach (RunState from in AllStates())
        {
            foreach (RunState to in AllStates())
            {
                bool allowed = RunStateMachine.CanTransition(from, to);
                bool documented = legal.Contains((from, to));

                if (allowed != documented)
                {
                    failures.Add($"{from} -> {to}: CanTransition={allowed}, expected={documented}");
                }
            }
        }

        Assert.True(failures.Count == 0, $"Transition matrix mismatches: {string.Join("; ", failures)}");
    }

    [Fact]
    public void Terminal_states_have_no_outgoing_transitions()
    {
        foreach (RunState terminal in TerminalStates)
        {
            Assert.Empty(RunStateMachine.AllowedTransitions(terminal));
        }
    }

    [Fact]
    public void Public_stage_path_includes_intermediate_lifecycle_states()
    {
        Assert.Equal(
            [RunState.Mapped, RunState.Calibrating, RunState.CalibrationPassed],
            RunStateMachine.FindPath(RunState.Mapped, RunState.CalibrationPassed));
        Assert.Equal(
            [RunState.ReadyForAnalysis, RunState.Analyzing, RunState.ReplicateConflict, RunState.RunInvalid],
            RunStateMachine.FindPath(RunState.ReadyForAnalysis, RunState.RunInvalid));
    }

    [Fact]
    public void Invalid_or_skipped_paths_are_rejected()
    {
        Assert.Null(RunStateMachine.FindPath(RunState.Exported, RunState.New));
        Assert.False(RunStateMachine.IsValidPath(
            [RunState.Mapped, RunState.CalibrationPassed],
            RunState.Mapped,
            RunState.CalibrationPassed));
    }

    [Theory]
    [InlineData(RunState.New, RunState.Analyzed)]
    [InlineData(RunState.Imported, RunState.Calibrating)]
    [InlineData(RunState.Mapped, RunState.ReadyForAnalysis)]
    [InlineData(RunState.CalibrationFailed, RunState.ControlQc)]
    [InlineData(RunState.CalibrationPassed, RunState.Analyzing)]
    [InlineData(RunState.ControlQcFailed, RunState.Calibrating)]
    [InlineData(RunState.ReadyForAnalysis, RunState.Analyzed)]
    [InlineData(RunState.Analyzing, RunState.ControlQc)]
    [InlineData(RunState.ReplicateConflict, RunState.Analyzed)]
    [InlineData(RunState.Analyzed, RunState.New)]
    [InlineData(RunState.Exported, RunState.New)]
    [InlineData(RunState.RunInvalid, RunState.New)]
    [InlineData(RunState.New, RunState.New)]
    [InlineData(RunState.Calibrating, RunState.RunInvalid)]
    [InlineData(RunState.ControlQc, RunState.RunInvalid)]
    [InlineData(RunState.Analyzing, RunState.RunInvalid)]
    public void Specific_illegal_transitions_return_false(RunState from, RunState to)
    {
        Assert.False(RunStateMachine.CanTransition(from, to));
    }

    public static TheoryData<RunState, RunState> LegalPairs()
    {
        var data = new TheoryData<RunState, RunState>();
        foreach ((RunState from, RunState to) in LegalTransitions)
        {
            data.Add(from, to);
        }

        return data;
    }
}
