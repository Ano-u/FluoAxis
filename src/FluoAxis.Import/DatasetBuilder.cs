namespace FluoAxis.Import;

using FluoAxis.Domain;

/// <summary>
/// Channel-neutral builder that turns streamed rows plus the confirmed
/// mapping into validated <see cref="SignalSeries"/>. Enforces the
/// Data-Contract guarantees: rows are consumed in physical file order and
/// duplicate or backwards timestamps are structured failures (never
/// silently repaired or re-ordered), empty or non-numeric required cells
/// fail closed, negative RFU is allowed, the first valid acquisition maps
/// to t=0, and FAM/ROX must cover the same wells on the same time axis.
/// </summary>
internal sealed class DatasetBuilder
{
    private readonly Guid _runId;

    public DatasetBuilder(Guid runId)
    {
        _runId = runId;
    }

    /// <summary>Row callback contract used while streaming a region.</summary>
    public interface IRowSource
    {
        /// <summary>
        /// Streams data rows (rows after the header row) of the mapped
        /// region in physical file order. Returns a structured failure
        /// instead of throwing when limits or parsing fail.
        /// </summary>
        IEnumerable<Result<RawRow>> StreamDataRows(int headerRowIndex, CancellationToken cancellationToken);
    }

    public Result<ImportedDataset> Build(
        ChannelRegionMapping famMapping,
        IRowSource famSource,
        ChannelRegionMapping roxMapping,
        IRowSource roxSource,
        CancellationToken cancellationToken)
    {
        if (!WellSetsMatch(famMapping, roxMapping, out AppError? setError))
        {
            return Result<ImportedDataset>.Fail(setError!);
        }

        Result<Dictionary<WellId, List<RawObservation>>> famData = CollectObservations(famMapping, famSource, cancellationToken);
        if (!famData.IsSuccess)
        {
            return Result<ImportedDataset>.Fail([.. famData.Errors]);
        }

        Result<Dictionary<WellId, List<RawObservation>>> roxData = CollectObservations(roxMapping, roxSource, cancellationToken);
        if (!roxData.IsSuccess)
        {
            return Result<ImportedDataset>.Fail([.. roxData.Errors]);
        }

        if (!TimeAxesMatch(famData.Value!, roxData.Value!, out AppError? axisError))
        {
            return Result<ImportedDataset>.Fail(axisError!);
        }

        var series = new List<SignalSeries>(famData.Value!.Count + roxData.Value!.Count);

        Result<List<SignalSeries>> famSeries = BuildChannelSeries(FluorescenceChannel.Fam, famData.Value!);
        if (!famSeries.IsSuccess)
        {
            return Result<ImportedDataset>.Fail([.. famSeries.Errors]);
        }

        series.AddRange(famSeries.Value!);

        Result<List<SignalSeries>> roxSeries = BuildChannelSeries(FluorescenceChannel.Rox, roxData.Value!);
        if (!roxSeries.IsSuccess)
        {
            return Result<ImportedDataset>.Fail([.. roxSeries.Errors]);
        }

        series.AddRange(roxSeries.Value!);

        return Result<ImportedDataset>.Ok(new ImportedDataset(series, []));
    }

    /// <summary>One raw time/value pair with its origin row number for error reporting.</summary>
    private readonly record struct RawObservation(double Minutes, double Rfu, int RowNumber);

    private static Result<Dictionary<WellId, List<RawObservation>>> CollectObservations(
        ChannelRegionMapping mapping,
        IRowSource source,
        CancellationToken cancellationToken)
    {
        var byWell = new Dictionary<WellId, List<RawObservation>>();
        foreach (WellId well in mapping.WellColumns.Values)
        {
            byWell[well] = [];
        }

        foreach (Result<RawRow> rowResult in source.StreamDataRows(mapping.HeaderRowIndex, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!rowResult.IsSuccess)
            {
                return Result<Dictionary<WellId, List<RawObservation>>>.Fail([.. rowResult.Errors]);
            }

            RawRow row = rowResult.Value!;
            if (!TryGetCell(row, mapping.TimeColumnIndex, out RawCell timeCell)
                || !timeCell.TryGetNumber(out double minutes))
            {
                return Result<Dictionary<WellId, List<RawObservation>>>.Fail(
                    ImportErrors.InvalidTimeValue(row.RowIndex + 1, mapping.TimeColumnIndex));
            }

            foreach ((int columnIndex, WellId well) in mapping.WellColumns)
            {
                if (!TryGetCell(row, columnIndex, out RawCell cell) || !cell.TryGetNumber(out double rfu))
                {
                    // Empty or non-numeric required cell: fail closed with well
                    // and position (Data-Contract: empty never becomes 0).
                    return Result<Dictionary<WellId, List<RawObservation>>>.Fail(
                        ImportErrors.InvalidValue(row.RowIndex + 1, columnIndex, $"well={well}"));
                }

                byWell[well].Add(new RawObservation(minutes, rfu, row.RowIndex + 1));
            }
        }

        return Result<Dictionary<WellId, List<RawObservation>>>.Ok(byWell);
    }

    private Result<List<SignalSeries>> BuildChannelSeries(
        FluorescenceChannel channel,
        Dictionary<WellId, List<RawObservation>> byWell)
    {
        var series = new List<SignalSeries>(byWell.Count);

        foreach (WellId well in byWell.Keys.Order(WellPlateOrderComparer.Instance))
        {
            List<RawObservation> observations = byWell[well];
            double t0 = observations.Count > 0 ? observations[0].Minutes : 0;
            var points = new List<SignalPoint>(observations.Count);
            double previousMinutes = double.NaN;

            foreach (RawObservation observation in observations)
            {
                if (!double.IsNaN(previousMinutes))
                {
                    if (observation.Minutes == previousMinutes)
                    {
                        return Result<List<SignalSeries>>.Fail(
                            ImportErrors.DuplicateTime(observation.RowNumber));
                    }

                    if (observation.Minutes < previousMinutes)
                    {
                        return Result<List<SignalSeries>>.Fail(
                            ImportErrors.NonMonotonicTime(observation.RowNumber));
                    }
                }

                points.Add(new SignalPoint(MinutesToTimeSpan(observation.Minutes - t0), observation.Rfu));
                previousMinutes = observation.Minutes;
            }

            SignalSeries created;
            try
            {
                created = SignalSeries.Create(_runId, well, channel, points);
            }
            catch (ArgumentException)
            {
                // SignalSeries.Create is the final authority on finiteness and
                // ordering; a violation here means our guards missed it.
                return Result<List<SignalSeries>>.Fail(
                    ImportErrors.InvalidNonFiniteRfu(well.ToString(), observations.Count > 0 ? observations[0].RowNumber : 0));
            }

            series.Add(created);
        }

        return Result<List<SignalSeries>>.Ok(series);
    }

    internal static TimeSpan MinutesToTimeSpan(double minutes)
    {
        if (!double.IsFinite(minutes))
        {
            throw new ArgumentOutOfRangeException(nameof(minutes), minutes, "Elapsed minutes must be finite.");
        }

        return new TimeSpan((long)Math.Round(minutes * TimeSpan.TicksPerMinute));
    }

    private static bool TryGetCell(RawRow row, int columnIndex, out RawCell cell)
    {
        foreach (RawCell candidate in row.Cells)
        {
            if (candidate.ColumnIndex == columnIndex)
            {
                cell = candidate;
                return true;
            }
        }

        cell = default;
        return false;
    }

    private static bool WellSetsMatch(ChannelRegionMapping fam, ChannelRegionMapping rox, out AppError? error)
    {
        var famWells = new HashSet<WellId>(fam.WellColumns.Values);
        var roxWells = new HashSet<WellId>(rox.WellColumns.Values);

        if (famWells.SetEquals(roxWells))
        {
            error = null;
            return true;
        }

        var missingInFam = string.Join(",", roxWells.Except(famWells).Order(WellPlateOrderComparer.Instance).Select(static w => w.ToString()));
        var missingInRox = string.Join(",", famWells.Except(roxWells).Order(WellPlateOrderComparer.Instance).Select(static w => w.ToString()));
        error = ImportErrors.WellSetMismatch(missingInFam, missingInRox);
        return false;
    }

    private static bool TimeAxesMatch(
        Dictionary<WellId, List<RawObservation>> famData,
        Dictionary<WellId, List<RawObservation>> roxData,
        out AppError? error)
    {
        foreach (WellId well in famData.Keys)
        {
            if (!roxData.TryGetValue(well, out List<RawObservation>? roxRows) || roxRows is null)
            {
                continue;
            }

            List<RawObservation> famRows = famData[well];
            if (famRows.Count != roxRows.Count)
            {
                error = ImportErrors.ChannelTimeAxisMismatch();
                return false;
            }

            for (int i = 0; i < famRows.Count; i++)
            {
                if (MinutesToTimeSpan(famRows[i].Minutes) != MinutesToTimeSpan(roxRows[i].Minutes))
                {
                    error = ImportErrors.ChannelTimeAxisMismatch();
                    return false;
                }
            }
        }

        error = null;
        return true;
    }

    /// <summary>Wells in plate order: A1..A12, B1..B12, ..., H1..H12.</summary>
    private sealed class WellPlateOrderComparer : IComparer<WellId>
    {
        public static WellPlateOrderComparer Instance { get; } = new();

        public int Compare(WellId x, WellId y)
        {
            int row = x.Row.CompareTo(y.Row);
            return row != 0 ? row : x.Column.CompareTo(y.Column);
        }
    }
}
