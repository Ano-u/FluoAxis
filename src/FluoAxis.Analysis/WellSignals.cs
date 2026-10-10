namespace FluoAxis.Analysis;

using FluoAxis.Domain;
using System.Collections.ObjectModel;

/// <summary>
/// The dual-channel kinetic data of a single well, as consumed by the
/// analysis engine. Both channels must be present; the engine treats
/// FAM and ROX symmetrically (the only asymmetry in the method is the
/// per-channel WT limit in <see cref="BrafMethodSnapshot.Wt"/>).
/// </summary>
public sealed record WellSignals
{
    public WellId Well { get; }
    public IReadOnlyDictionary<FluorescenceChannel, SignalSeries> Channels { get; }

    public WellSignals(WellId well, IReadOnlyDictionary<FluorescenceChannel, SignalSeries> channels)
    {
        ArgumentNullException.ThrowIfNull(channels);

        if (!channels.ContainsKey(FluorescenceChannel.Fam) || !channels.ContainsKey(FluorescenceChannel.Rox))
        {
            throw new ArgumentException("Both FAM and ROX series are required for every analyzed well.", nameof(channels));
        }

        foreach (KeyValuePair<FluorescenceChannel, SignalSeries> pair in channels)
        {
            if (pair.Value.Well != well)
            {
                throw new ArgumentException($"The series for channel {pair.Key} belongs to well {pair.Value.Well}, not {well}.", nameof(channels));
            }

            if (pair.Value.Channel != pair.Key)
            {
                throw new ArgumentException($"The series stored under channel {pair.Key} declares channel {pair.Value.Channel}.", nameof(channels));
            }
        }

        SignalSeries fam = channels[FluorescenceChannel.Fam];
        SignalSeries rox = channels[FluorescenceChannel.Rox];
        if (fam.RunId != rox.RunId)
        {
            throw new ArgumentException("FAM and ROX series must belong to the same Run.", nameof(channels));
        }

        if (fam.Points.Count != rox.Points.Count
            || fam.Points.Zip(rox.Points).Any(pair => pair.First.Elapsed != pair.Second.Elapsed))
        {
            throw new ArgumentException("FAM and ROX series must share the same validated time axis.", nameof(channels));
        }

        Well = well;
        Channels = new ReadOnlyDictionary<FluorescenceChannel, SignalSeries>(channels.ToDictionary());
    }
}

/// <summary>
/// One structured per-well diagnostic produced during an analysis stage:
/// what code fired, on which channel, with what measured value. Used for
/// QC displays and reports; never a concatenated message string.
/// </summary>
public sealed record WellDiagnostic
{
    public WellId Well { get; }
    public FluorescenceChannel Channel { get; }
    public string Code { get; }
    public string Message { get; }
    public double? Value { get; }

    public WellDiagnostic(WellId well, FluorescenceChannel channel, string code, string message, double? value = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        Well = well;
        Channel = channel;
        Code = code;
        Message = message;
        Value = value;
    }
}
