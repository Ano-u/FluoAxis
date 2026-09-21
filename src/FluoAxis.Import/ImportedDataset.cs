namespace FluoAxis.Import;

using FluoAxis.Domain;

/// <summary>
/// Result of a confirmed import read: the validated per-well, per-channel
/// kinetic series plus non-blocking diagnostics gathered during the read
/// (e.g. ragged rows skipped as diagnostics instead of failing the file).
/// </summary>
public sealed record ImportedDataset
{
    /// <summary>One series per mapped well and channel, in plate order.</summary>
    public IReadOnlyList<SignalSeries> Series { get; }

    /// <summary>Non-blocking diagnostics (warnings) collected during the read.</summary>
    public IReadOnlyList<AppError> Diagnostics { get; }

    public ImportedDataset(IReadOnlyList<SignalSeries> series, IReadOnlyList<AppError> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(series);
        ArgumentNullException.ThrowIfNull(diagnostics);

        Series = series;
        Diagnostics = diagnostics;
    }
}
