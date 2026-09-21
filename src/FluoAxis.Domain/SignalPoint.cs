namespace FluoAxis.Domain;

/// <summary>
/// One acquired fluorescence measurement: elapsed time since the first
/// valid acquisition (t=0) and raw RFU intensity.
/// </summary>
public readonly record struct SignalPoint(TimeSpan Elapsed, double Rfu);
