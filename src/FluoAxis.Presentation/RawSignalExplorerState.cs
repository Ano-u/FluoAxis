namespace FluoAxis.Presentation;

using FluoAxis.Contracts;

/// <summary>One bounded display page; never owns a full raw dataset or modifies a scientific result.</summary>
public sealed class RawSignalExplorerState
{
    public RawSignalPageDto? Page { get; private set; }
    public int Offset { get; private set; }
    public bool CanPrevious => Page is not null && Offset > 0;
    public bool CanNext => Page is { } page && page.Points.Count > 0 && (long)Offset + page.Points.Count < page.TotalPoints;

    public bool TryApply(RawSignalPageDto page, Guid runId, int revision, string well, string channel, int offset)
    {
        ArgumentNullException.ThrowIfNull(page);
        if (offset < 0 || page.RunId != runId || page.Revision != revision || page.Well != well || page.Channel != channel) return false;
        Page = page;
        Offset = offset;
        return true;
    }

    public void Clear() { Page = null; Offset = 0; }
    public static string PointId(RawSignalPageDto page, RawSignalPointDto point) => $"raw-{page.RunId:N}-{page.Well}-{page.Channel}-{point.PointIndex}";
    public static double DisplayMinutes(RawSignalPointDto point) => (double)point.ElapsedTicks / TimeSpan.TicksPerMinute;
}
