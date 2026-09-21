namespace FluoAxis.Domain;

/// <summary>
/// Immutable snapshot of the BRAF V600E research method parameters.
/// This record is the single in-code source of the scientific constants;
/// the default values mirror Method-Specification V1.0 appendix JSON.
/// </summary>
public sealed record BrafMethodSnapshot
{
    public const string DefaultMethodId = "BRAF-V600E-Research";
    public const string DefaultVersion = "1.0";

    public string MethodId { get; init; }
    public string Version { get; init; }
    public double EndpointMinutes { get; init; }
    /// <summary>Closed tolerance on either side of the endpoint, measured in seconds.</summary>
    public double EndpointToleranceSeconds { get; init; }
    public PlateauSpec Plateau { get; init; }
    public NtcSpec Ntc { get; init; }
    public WtSpec Wt { get; init; }
    public ClassificationSpec Classification { get; init; }
    public ReplicateSpec Replicates { get; init; }
    public RoleCardinalitySpec Roles { get; init; }

    public BrafMethodSnapshot(
        string? methodId = null,
        string? version = null,
        double endpointMinutes = 120.0,
        double endpointToleranceSeconds = 15.0,
        PlateauSpec? plateau = null,
        NtcSpec? ntc = null,
        WtSpec? wt = null,
        ClassificationSpec? classification = null,
        ReplicateSpec? replicates = null,
        RoleCardinalitySpec? roles = null)
    {
        MethodId = string.IsNullOrWhiteSpace(methodId) ? DefaultMethodId : methodId;
        Version = string.IsNullOrWhiteSpace(version) ? DefaultVersion : version;
        EndpointMinutes = endpointMinutes;
        EndpointToleranceSeconds = endpointToleranceSeconds;
        Plateau = plateau ?? PlateauSpec.Default;
        Ntc = ntc ?? NtcSpec.Default;
        Wt = wt ?? WtSpec.Default;
        Classification = classification ?? ClassificationSpec.Default;
        Replicates = replicates ?? ReplicateSpec.Default;
        Roles = roles ?? RoleCardinalitySpec.Default;

        Validate();
    }

    /// <summary>Method V1.0 default parameter set (Method-Specification V1.0 appendix JSON).</summary>
    public static readonly BrafMethodSnapshot Default = new();

    /// <summary>
    /// SHA-256 (lowercase hex) over the canonical JSON serialization of this
    /// snapshot: fixed property order, no whitespace, invariant number
    /// formatting. Equal snapshots always produce equal hashes.
    /// </summary>
    public string ComputeCanonicalHash() => HexHash.FromText(MethodSnapshotJson.WriteCanonicalJson(this)).Value;

    private void Validate()
    {
        if (!double.IsFinite(EndpointMinutes) || EndpointMinutes <= 0)
        {
            throw new ArgumentException("EndpointMinutes must be finite and positive.", nameof(EndpointMinutes));
        }

        if (!double.IsFinite(EndpointToleranceSeconds) || EndpointToleranceSeconds < 0)
        {
            throw new ArgumentException("EndpointToleranceSeconds must be finite and non-negative.", nameof(EndpointToleranceSeconds));
        }

        if (Plateau.StartMinutes >= Plateau.EndMinutes)
        {
            throw new ArgumentException("Plateau window start must be strictly before end.", nameof(Plateau));
        }

        if (Plateau.EndMinutes > EndpointMinutes)
        {
            throw new ArgumentException("Plateau window end cannot exceed the endpoint time.", nameof(Plateau));
        }
    }
}

/// <summary>Plateau (100% positive) flatness window and pass criterion.</summary>
public sealed record PlateauSpec
{
    public double StartMinutes { get; }
    public double EndMinutes { get; }
    public double RelativeRangeMax { get; }
    public int MinWindowPoints { get; }

    public PlateauSpec(
        double startMinutes = 90.0,
        double endMinutes = 120.0,
        double relativeRangeMax = 0.01,
        int minWindowPoints = 2)
    {
        StartMinutes = startMinutes;
        EndMinutes = endMinutes;
        RelativeRangeMax = relativeRangeMax;
        MinWindowPoints = minWindowPoints;

        if (!double.IsFinite(startMinutes) || !double.IsFinite(endMinutes))
        {
            throw new ArgumentException("Plateau window bounds must be finite.", nameof(startMinutes));
        }

        if (startMinutes >= endMinutes)
        {
            throw new ArgumentException("Plateau window start must be strictly before end.", nameof(startMinutes));
        }

        if (!double.IsFinite(relativeRangeMax) || relativeRangeMax < 0)
        {
            throw new ArgumentException("RelativeRangeMax must be finite and non-negative.", nameof(relativeRangeMax));
        }

        if (minWindowPoints < 1)
        {
            throw new ArgumentException("MinWindowPoints must be at least 1.", nameof(minWindowPoints));
        }
    }

    public static PlateauSpec Default { get; } = new();
}

/// <summary>NTC full-course normalized signal acceptance band.</summary>
public sealed record NtcSpec
{
    public double NormalizedMin { get; }
    public double NormalizedMax { get; }

    public NtcSpec(double normalizedMin = -0.05, double normalizedMax = 0.05)
    {
        NormalizedMin = normalizedMin;
        NormalizedMax = normalizedMax;

        if (!double.IsFinite(normalizedMin) || !double.IsFinite(normalizedMax))
        {
            throw new ArgumentException("NTC band bounds must be finite.", nameof(normalizedMin));
        }

        if (normalizedMin >= normalizedMax)
        {
            throw new ArgumentException("NTC band minimum must be strictly below maximum.", nameof(normalizedMin));
        }
    }

    public static NtcSpec Default { get; } = new();
}

/// <summary>WT negative-control group mean acceptance limits (exclusive).</summary>
public sealed record WtSpec
{
    public double FamMeanMaxExclusive { get; }
    public double RoxMeanMaxExclusive { get; }

    public WtSpec(double famMeanMaxExclusive = 0.05, double roxMeanMaxExclusive = 0.05)
    {
        FamMeanMaxExclusive = famMeanMaxExclusive;
        RoxMeanMaxExclusive = roxMeanMaxExclusive;

        if (!double.IsFinite(famMeanMaxExclusive) || !double.IsFinite(roxMeanMaxExclusive))
        {
            throw new ArgumentException("WT limits must be finite.", nameof(famMeanMaxExclusive));
        }
    }

    public static WtSpec Default { get; } = new();
}

/// <summary>dr' classification threshold and official result labels.</summary>
public sealed record ClassificationSpec
{
    public double DrPrimeThresholdInclusive { get; }
    public string PositiveLabel { get; }
    public string NegativeLabel { get; }

    public ClassificationSpec(
        double drPrimeThresholdInclusive = 0.4,
        string? positiveLabel = null,
        string? negativeLabel = null)
    {
        DrPrimeThresholdInclusive = drPrimeThresholdInclusive;
        PositiveLabel = string.IsNullOrWhiteSpace(positiveLabel) ? "BRAF V600E 阳性" : positiveLabel;
        NegativeLabel = string.IsNullOrWhiteSpace(negativeLabel) ? "BRAF V600E 阴性" : negativeLabel;

        if (!double.IsFinite(drPrimeThresholdInclusive) || drPrimeThresholdInclusive < 0)
        {
            throw new ArgumentException("DrPrimeThresholdInclusive must be finite and non-negative.", nameof(drPrimeThresholdInclusive));
        }

        if (string.IsNullOrWhiteSpace(PositiveLabel) || string.IsNullOrWhiteSpace(NegativeLabel))
        {
            throw new ArgumentException("Classification labels must be non-empty.", nameof(positiveLabel));
        }
    }

    public static ClassificationSpec Default { get; } = new();
}

/// <summary>Replicate direction-consistency policy for LQC and sample groups.</summary>
public sealed record ReplicateSpec
{
    public bool RequireClassificationConsistency { get; }

    public ReplicateSpec(bool requireClassificationConsistency = true)
    {
        RequireClassificationConsistency = requireClassificationConsistency;
    }

    public static ReplicateSpec Default { get; } = new();
}

/// <summary>Minimum well counts per required role (fail-closed cardinality).</summary>
public sealed record RoleCardinalitySpec
{
    public int NtcMinimumWells { get; }
    public int Positive100MinimumWells { get; }
    public int WildTypeMinimumWells { get; }
    public int Lqc01PercentMinimumWells { get; }

    public RoleCardinalitySpec(
        int ntcMinimumWells = 1,
        int positive100MinimumWells = 1,
        int wildTypeMinimumWells = 1,
        int lqc01PercentMinimumWells = 1)
    {
        NtcMinimumWells = ntcMinimumWells;
        Positive100MinimumWells = positive100MinimumWells;
        WildTypeMinimumWells = wildTypeMinimumWells;
        Lqc01PercentMinimumWells = lqc01PercentMinimumWells;

        if (ntcMinimumWells < 0 || positive100MinimumWells < 0 || wildTypeMinimumWells < 0 || lqc01PercentMinimumWells < 0)
        {
            throw new ArgumentException("Role minimum well counts must be non-negative.", nameof(ntcMinimumWells));
        }
    }

    public static RoleCardinalitySpec Default { get; } = new();
}
