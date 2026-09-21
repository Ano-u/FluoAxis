namespace FluoAxis.PlotProtocol;

/// <summary>Host-side suppression of stale generations and uncorrelated command responses.</summary>
public sealed class PlotResponseGate
{
    private readonly Dictionary<Guid, PlotState> plots = [];

    public void Activate(Guid plotId, long generation)
    {
        Guard.NonEmpty(plotId, nameof(plotId));
        ArgumentOutOfRangeException.ThrowIfNegative(generation);
        plots[plotId] = new PlotState(generation);
    }

    public void RegisterRequest(Guid plotId, long generation, Guid requestId)
    {
        Guard.NonEmpty(requestId, nameof(requestId));
        if (!plots.TryGetValue(plotId, out PlotState? state) || state.Generation != generation)
        {
            throw new InvalidOperationException("Request generation is not active for the plot.");
        }

        if (!state.Requests.Add(requestId)) throw new InvalidOperationException("Request identifier is already outstanding.");
    }

    /// <summary>Registers only commands that have a correlated terminal response in protocol V1.</summary>
    public bool RegisterCommand(Guid plotId, long generation, Guid requestId, string commandType)
    {
        if (!HasCorrelatedResponse(commandType))
        {
            return false;
        }

        RegisterRequest(plotId, generation, requestId);
        return true;
    }

    public bool TryAccept(PlotMessageEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (!plots.TryGetValue(envelope.PlotId, out PlotState? state) || state.Generation != envelope.Generation) return false;

        if (RequiresOutstandingRequest(envelope.Type))
        {
            return state.Requests.Remove(envelope.RequestId);
        }

        if (IsCorrelatedOrUnsolicitedEvent(envelope.Type))
        {
            state.Requests.Remove(envelope.RequestId);
            return true;
        }

        return IsUnsolicitedEvent(envelope.Type);
    }

    public void Dispose(Guid plotId) => plots.Remove(plotId);

    private static bool RequiresOutstandingRequest(string type) => type is
        PlotMessageTypes.Rendered or PlotMessageTypes.Error or PlotMessageTypes.ExportCompleted;

    private static bool HasCorrelatedResponse(string type) => type is
        PlotMessageTypes.Render or PlotMessageTypes.Export or PlotMessageTypes.ResetCamera or PlotMessageTypes.SetSelection;

    private static bool IsUnsolicitedEvent(string type) => type is
        PlotMessageTypes.Ready or PlotMessageTypes.PointClicked;

    private static bool IsCorrelatedOrUnsolicitedEvent(string type) => type is
        PlotMessageTypes.SelectionChanged or PlotMessageTypes.CameraChanged;

    private sealed record PlotState(long Generation)
    {
        public HashSet<Guid> Requests { get; } = [];
    }
}
