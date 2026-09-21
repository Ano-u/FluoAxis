namespace FluoAxis.Import;

using FluoAxis.Domain;

/// <summary>
/// User-confirmed mapping of one channel to its source region: the sheet
/// name, header row index, time column, and the set of well columns.
/// This is the authoritative input to <c>ReadAsync</c>; sheet-name channel
/// suggestions from the preview are ignored here.
/// </summary>
public sealed record ChannelRegionMapping
{
    public FluorescenceChannel Channel { get; }

    /// <summary>Sheet name (XLSX) or the body label (CSV/TSV/TXT) the channel data lives in.</summary>
    public string SheetName { get; }

    /// <summary>0-based row index of the header row inside the region.</summary>
    public int HeaderRowIndex { get; }

    /// <summary>0-based column index holding elapsed time in minutes.</summary>
    public int TimeColumnIndex { get; }

    /// <summary>0-based column indices of wells included in this channel, mapped to their well identifiers.</summary>
    public IReadOnlyDictionary<int, WellId> WellColumns { get; }

    public ChannelRegionMapping(
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

        if (headerRowIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(headerRowIndex), headerRowIndex, "Header row index must not be negative.");
        }

        if (timeColumnIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(timeColumnIndex), timeColumnIndex, "Time column index must not be negative.");
        }

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

/// <summary>
/// The complete user-confirmed import mapping: per-channel region mapping
/// plus the run the data belongs to. ReadAsync trusts only this object.
/// </summary>
public sealed record DataMapping
{
    public Guid RunId { get; }

    public ChannelRegionMapping Fam { get; }

    public ChannelRegionMapping Rox { get; }

    public DataMapping(Guid runId, ChannelRegionMapping fam, ChannelRegionMapping rox)
    {
        if (runId == Guid.Empty)
        {
            throw new ArgumentException("RunId must not be empty.", nameof(runId));
        }

        ArgumentNullException.ThrowIfNull(fam);
        ArgumentNullException.ThrowIfNull(rox);

        if (fam.Channel != FluorescenceChannel.Fam)
        {
            throw new ArgumentException("The fam mapping must map the Fam channel.", nameof(fam));
        }

        if (rox.Channel != FluorescenceChannel.Rox)
        {
            throw new ArgumentException("The rox mapping must map the Rox channel.", nameof(rox));
        }

        RunId = runId;
        Fam = fam;
        Rox = rox;
    }
}
