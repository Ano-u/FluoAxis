namespace FluoAxis.Analysis;

using FluoAxis.Domain;

/// <summary>One observation actually visited by the deterministic calculation; no display resampling or recomputation.</summary>
public readonly record struct EvaluatedPoint(int PointIndex, SignalPoint Point, double? Normalized = null, bool? WithinBand = null);

public readonly record struct ChannelEndpoint(FluorescenceChannel Channel, int PointIndex, SignalPoint Point);
