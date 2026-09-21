namespace FluoAxis.Domain;

/// <summary>
/// Sample-group metadata of a plate assignment (FR-PLATE-005): which
/// specimen a well set belongs to, its replicate ordinal, and optional
/// researcher annotations.
/// </summary>
public sealed record SampleAssignment
{
    public string SampleId { get; }
    public string? Group { get; }
    public int ReplicateIndex { get; }
    public string? Target { get; }
    public string? Notes { get; }

    public SampleAssignment(string sampleId, string? group, int replicateIndex, string? target, string? notes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sampleId);

        if (replicateIndex < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(replicateIndex), replicateIndex, "ReplicateIndex is 1-based and must be at least 1.");
        }

        SampleId = sampleId;
        Group = group;
        ReplicateIndex = replicateIndex;
        Target = target;
        Notes = notes;
    }
}
