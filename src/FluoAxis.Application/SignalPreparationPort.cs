namespace FluoAxis.Application;

using FluoAxis.Contracts;
using FluoAxis.Domain;

/// <summary>
/// Identity of one managed-relative source resource that has already been
/// staged beneath a managed root (Data-Contract §1.2/§3.8). The original
/// machine location never appears here; only the managed relative reference,
/// its verified SHA-256, and the size observed while staging do.
/// </summary>
public sealed record PreparedSourceIdentity
{
    public string DisplayName { get; }
    public RelativePath ResourcePath { get; }
    public string Sha256 { get; }
    public long SizeBytes { get; }
    public string MediaType { get; }

    public PreparedSourceIdentity(
        string displayName,
        RelativePath resourcePath,
        string sha256,
        long sizeBytes,
        string mediaType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentNullException.ThrowIfNull(resourcePath);
        ArgumentNullException.ThrowIfNull(mediaType);

        if (displayName.IndexOfAny(['/', '\\']) >= 0)
        {
            throw new ArgumentException("DisplayName must be a bare file name, not a path.", nameof(displayName));
        }

        HexHash hash = new(sha256);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sizeBytes);

        DisplayName = displayName;
        ResourcePath = resourcePath;
        Sha256 = hash.Value;
        SizeBytes = sizeBytes;
        MediaType = mediaType;
    }
}

/// <summary>
/// The confirmed channel mapping of one source region, expressed in
/// Application-owned logical coordinates. This mirrors the Import adapter's
/// own mapping shape without leaking the <c>FluoAxis.Import</c> assembly
/// across the Application boundary (the architecture test forbids that
/// reference).
/// </summary>
public sealed record PreparedChannelRegion
{
    public FluorescenceChannel Channel { get; }

    /// <summary>Sheet name (XLSX) or the body label (CSV/TSV/TXT).</summary>
    public string SheetName { get; }

    /// <summary>0-based row index of the header row inside the region.</summary>
    public int HeaderRowIndex { get; }

    /// <summary>0-based column index holding elapsed time in minutes.</summary>
    public int TimeColumnIndex { get; }

    /// <summary>0-based column index to logical well, for every well included in this region.</summary>
    public IReadOnlyDictionary<int, WellId> WellColumns { get; }

    public PreparedChannelRegion(
        FluorescenceChannel channel,
        string sheetName,
        int headerRowIndex,
        int timeColumnIndex,
        IReadOnlyDictionary<int, WellId> wellColumns)
    {
        if (!Enum.IsDefined(channel))
        {
            throw new ArgumentOutOfRangeException(nameof(channel), channel, "Channel must be Fam or Rox.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(sheetName);
        ArgumentNullException.ThrowIfNull(wellColumns);
        ArgumentOutOfRangeException.ThrowIfNegative(headerRowIndex);
        ArgumentOutOfRangeException.ThrowIfNegative(timeColumnIndex);

        if (wellColumns.Count == 0)
        {
            throw new ArgumentException("At least one well column must be mapped.", nameof(wellColumns));
        }

        if (wellColumns.ContainsKey(timeColumnIndex))
        {
            throw new ArgumentException("The time column must not also be a well column.", nameof(wellColumns));
        }

        Channel = channel;
        SheetName = sheetName;
        HeaderRowIndex = headerRowIndex;
        TimeColumnIndex = timeColumnIndex;
        WellColumns = wellColumns;
    }
}

/// <summary>The complete confirmed mapping of one import into a run.</summary>
public sealed record PreparedDataMapping
{
    public Guid RunId { get; }
    public PreparedChannelRegion Fam { get; }
    public PreparedChannelRegion Rox { get; }

    public PreparedDataMapping(Guid runId, PreparedChannelRegion fam, PreparedChannelRegion rox)
    {
        if (runId == Guid.Empty)
        {
            throw new ArgumentException("RunId must not be empty.", nameof(runId));
        }

        ArgumentNullException.ThrowIfNull(fam);
        ArgumentNullException.ThrowIfNull(rox);

        if (fam.Channel != FluorescenceChannel.Fam)
        {
            throw new ArgumentException("The FAM region must identify the FAM channel.", nameof(fam));
        }

        if (rox.Channel != FluorescenceChannel.Rox)
        {
            throw new ArgumentException("The ROX region must identify the ROX channel.", nameof(rox));
        }

        string[] famWells = [.. fam.WellColumns.Values.Select(well => well.ToString()).Order(StringComparer.Ordinal)];
        string[] roxWells = [.. rox.WellColumns.Values.Select(well => well.ToString()).Order(StringComparer.Ordinal)];
        if (!famWells.SequenceEqual(roxWells, StringComparer.Ordinal))
        {
            throw new ArgumentException("FAM and ROX mappings must cover the same logical wells.", nameof(rox));
        }

        RunId = runId;
        Fam = fam;
        Rox = rox;
    }
}

/// <summary>A request to read one staged resource into validated per-well series.</summary>
public sealed record PreparedSignalRequest
{
    public PreparedSourceIdentity Source { get; }
    public PreparedDataMapping Mapping { get; }

    public PreparedSignalRequest(PreparedSourceIdentity source, PreparedDataMapping mapping)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(mapping);

        Source = source;
        Mapping = mapping;
    }
}

/// <summary>
/// The validated per-well, per-channel series read from one staged resource,
/// plus the source identity and confirmed mapping they came from. It carries
/// only Domain types (<see cref="SignalSeries"/>), so the Infrastructure
/// adapter that produces it never needs to know about the Analysis engine:
/// the Application layer builds the engine's <c>WellSignals</c> when it
/// groups the series by the role each well plays.
/// </summary>
public sealed record PreparedRunSignals
{
    public PreparedSourceIdentity Source { get; }
    public PreparedDataMapping Mapping { get; }

    /// <summary>One series per well and channel, in plate order.</summary>
    public IReadOnlyList<SignalSeries> Series { get; }

    /// <summary>Non-blocking diagnostics collected while reading (e.g. ragged rows skipped).</summary>
    public IReadOnlyList<AppError> Diagnostics { get; }

    public PreparedRunSignals(
        PreparedSourceIdentity source,
        PreparedDataMapping mapping,
        IReadOnlyList<SignalSeries> series,
        IReadOnlyList<AppError> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(mapping);

        Source = source;
        Mapping = mapping;
        Series = series ?? [];
        Diagnostics = diagnostics ?? [];
    }
}

/// <summary>
/// Application-owned port that turns one confirmed mapping plus a staged
/// managed resource into validated per-well signals. Implemented by
/// Infrastructure, which is the only layer allowed to see both the import
/// adapters and their result types. The Application layer never learns how
/// the bytes were parsed, and never sees a machine filesystem location.
/// </summary>
public interface ISignalPreparationPort
{
    Task<Result<PreparedRunSignals>> PrepareAsync(
        PreparedSignalRequest request,
        IProgress<OperationProgressDto>? progress,
        CancellationToken cancellationToken);
}
