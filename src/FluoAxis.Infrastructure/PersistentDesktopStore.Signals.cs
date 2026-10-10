namespace FluoAxis.Infrastructure;

using FluoAxis.Application;
using FluoAxis.Contracts;
using FluoAxis.Domain;
using FluoAxis.Persistence;

public sealed partial class PersistentDesktopStore
{
    public Task<GatewayResult<RawSignalPageDto>> GetRawSignalPageAsync(Guid runId, string well,
        string channel, int offset, int pageSize, CancellationToken cancellationToken) => ExecuteAsync(async () =>
    {
        WellId selectedWell;
        FluorescenceChannel selectedChannel;
        try
        {
            if (runId == Guid.Empty || offset < 0 || pageSize is < 1 or > RawSignalPageDto.MaximumPageSize)
                return RawPageFailure("PLOT_RAW_QUERY_INVALID", "原始数据分页参数无效，请检查所选 Run、偏移和点数上限。");
            selectedWell = WellId.Parse(well);
            selectedChannel = channel switch
            {
                ChannelNames.Fam => FluorescenceChannel.Fam,
                ChannelNames.Rox => FluorescenceChannel.Rox,
                _ => throw new ArgumentException("Unknown raw channel."),
            };
        }
        catch (Exception error) when (error is ArgumentException or FormatException)
        {
            return RawPageFailure("PLOT_RAW_QUERY_INVALID", "请选择有效的孔位和 FAM/ROX 通道。");
        }

        ProjectRunAggregate? before = await repository.GetByRunIdAsync(runId, cancellationToken).ConfigureAwait(false);
        if (before is null) return RawPageFailure(GatewayErrorCodes.RunNotFound, "所选 Run 不存在，请刷新项目列表。");
        StoredRawSignalPage page = await repository.GetRawSignalPageAsync(runId, selectedWell, selectedChannel,
            offset, pageSize, cancellationToken).ConfigureAwait(false);
        ProjectRunAggregate? after = await repository.GetByRunIdAsync(runId, cancellationToken).ConfigureAwait(false);
        if (after is null || before.RunRevision != after.RunRevision || before.RunState != after.RunState
            || before.Method.ComputeCanonicalHash() != after.Method.ComputeCanonicalHash())
            return RawPageFailure(GatewayErrorCodes.StaleRunRevision, "读取期间 Run 已更新，请刷新原始数据页。");
        return GatewayResult.Ok(new RawSignalPageDto(runId, before.RunRevision, selectedWell.ToString(), channel,
            page.TotalPoints, [.. page.Points.Select(point => new RawSignalPointDto(point.PointIndex, point.ElapsedTicks, point.Rfu))]));
    }, cancellationToken);

    private static GatewayResult<RawSignalPageDto> RawPageFailure(string code, string message) =>
        GatewayResult.Fail<RawSignalPageDto>(new AppErrorDto(code, SeverityNames.Blocking, message, "刷新所选 Run 后重试；此查询不会修改原始 RFU。"));
}
