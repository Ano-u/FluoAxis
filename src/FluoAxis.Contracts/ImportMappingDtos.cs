namespace FluoAxis.Contracts;

/// <summary>A 0-based source column explicitly bound to one logical 96-well identifier.</summary>
public sealed record WellColumnMappingDto
{
    public int ColumnIndex { get; }
    public string Well { get; }

    public WellColumnMappingDto(int columnIndex, string well)
    {
        ContractGuard.RequireNonNegative(columnIndex, nameof(columnIndex));
        ContractGuard.RequireNonBlank(well, nameof(well));

        ColumnIndex = columnIndex;
        Well = FluoAxis.Domain.WellId.Parse(well).ToString();
    }
}

/// <summary>One user-confirmed channel region in source logical coordinates.</summary>
public sealed record ChannelRegionMappingDto
{
    public string Channel { get; }
    public string SheetName { get; }
    public int HeaderRowIndex { get; }
    public int TimeColumnIndex { get; }
    public int? EndRowIndex { get; }
    public IReadOnlyList<WellColumnMappingDto> WellColumns { get; }

    public ChannelRegionMappingDto(
        string channel,
        string sheetName,
        int headerRowIndex,
        int timeColumnIndex,
        IReadOnlyList<WellColumnMappingDto> wellColumns,
        int? endRowIndex = null)
    {
        ContractGuard.RequireNonBlank(channel, nameof(channel));
        ContractGuard.RequireChannel(channel);
        ContractGuard.RequireNonBlank(sheetName, nameof(sheetName));
        ContractGuard.RequireNonNegative(headerRowIndex, nameof(headerRowIndex));
        ContractGuard.RequireNonNegative(timeColumnIndex, nameof(timeColumnIndex));
        if (endRowIndex is { } end && end <= headerRowIndex)
            throw new ArgumentOutOfRangeException(nameof(endRowIndex), "The inclusive end row must be after the header.");
        ArgumentNullException.ThrowIfNull(wellColumns);

        if (wellColumns.Count == 0)
        {
            throw new ArgumentException("At least one well column must be mapped.", nameof(wellColumns));
        }

        WellColumnMappingDto[] snapshot = [.. wellColumns];
        if (snapshot.Any(item => item is null))
        {
            throw new ArgumentException("Well-column mappings must not contain null entries.", nameof(wellColumns));
        }

        if (snapshot.Any(item => item.ColumnIndex == timeColumnIndex))
        {
            throw new ArgumentException("The time column must not also be a well column.", nameof(wellColumns));
        }

        if (snapshot.Select(item => item.ColumnIndex).Distinct().Count() != snapshot.Length)
        {
            throw new ArgumentException("Each source column may be mapped only once.", nameof(wellColumns));
        }

        if (snapshot.Select(item => item.Well).Distinct(StringComparer.Ordinal).Count() != snapshot.Length)
        {
            throw new ArgumentException("Each logical well may be mapped only once per channel.", nameof(wellColumns));
        }

        Channel = channel;
        SheetName = sheetName;
        HeaderRowIndex = headerRowIndex;
        TimeColumnIndex = timeColumnIndex;
        EndRowIndex = endRowIndex;
        WellColumns = Array.AsReadOnly(snapshot);
    }
}

/// <summary>An advisory region discovered during inspection; confirmation remains authoritative.</summary>
public sealed record RegionCandidateDto
{
    public string SheetName { get; }
    public int? HeaderRowIndex { get; }
    public int? TimeColumnIndex { get; }
    public IReadOnlyList<WellColumnMappingDto> WellColumns { get; }
    public string? SuggestedChannel { get; }
    public long EstimatedDataRows { get; }

    public RegionCandidateDto(
        string sheetName,
        int? headerRowIndex,
        int? timeColumnIndex,
        IReadOnlyList<WellColumnMappingDto>? wellColumns = null,
        string? suggestedChannel = null,
        long estimatedDataRows = 0)
    {
        ContractGuard.RequireNonBlank(sheetName, nameof(sheetName));
        if (headerRowIndex is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(headerRowIndex));
        }

        if (timeColumnIndex is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(timeColumnIndex));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(estimatedDataRows);

        ContractGuard.RequireChannel(suggestedChannel);
        WellColumnMappingDto[] snapshot = [.. wellColumns ?? []];
        if (snapshot.Any(item => item is null))
        {
            throw new ArgumentException("Well-column mappings must not contain null entries.", nameof(wellColumns));
        }

        if (snapshot.Select(item => item.ColumnIndex).Distinct().Count() != snapshot.Length
            || snapshot.Select(item => item.Well).Distinct(StringComparer.Ordinal).Count() != snapshot.Length)
        {
            throw new ArgumentException("Candidate well columns must have unique columns and wells.", nameof(wellColumns));
        }

        if (timeColumnIndex is { } time && snapshot.Any(item => item.ColumnIndex == time))
        {
            throw new ArgumentException("The candidate time column must not also be a well column.", nameof(wellColumns));
        }

        SheetName = sheetName;
        HeaderRowIndex = headerRowIndex;
        TimeColumnIndex = timeColumnIndex;
        WellColumns = Array.AsReadOnly(snapshot);
        SuggestedChannel = suggestedChannel;
        EstimatedDataRows = estimatedDataRows;
    }
}
