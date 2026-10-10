namespace FluoAxis.Infrastructure;

using System.Security.Cryptography;
using System.Text.Json;
using FluoAxis.Contracts;
using FluoAxis.Domain;
using FluoAxis.Persistence;

public sealed partial class PersistentDesktopStore
{
    private const int MaximumExplorerFileBytes = 4 * 1024 * 1024;

    public Task<GatewayResult<ExplorerPairPageDto>> GetExplorerPairPageAsync(Guid runId, string well, int offset, int pageSize, CancellationToken token) =>
        ExecuteAsync(async () =>
        {
            if (offset < 0 || pageSize is < 1 or > 25000) return Failure<ExplorerPairPageDto>("PLOT_EXPLORER_PAGE_INVALID", "分页参数超出范围。");
            WellId parsed;
            try { parsed = WellId.Parse(well); }
            catch (Exception error) when (error is FormatException or ArgumentException) { return Failure<ExplorerPairPageDto>("PLOT_EXPLORER_WELL_INVALID", "孔位无效。"); }
            var before = await repository.GetByRunIdAsync(runId, token).ConfigureAwait(false);
            var source = await repository.GetSourceAsync(runId, token).ConfigureAwait(false);
            if (before is null || source is null) return Failure<ExplorerPairPageDto>("PLOT_EXPLORER_SOURCE_UNAVAILABLE", "当前 Run 尚无保存的原始信号。");
            StoredRawSignalPage fam = await repository.GetRawSignalPageAsync(runId, parsed, FluorescenceChannel.Fam, offset, pageSize, token).ConfigureAwait(false);
            StoredRawSignalPage rox = await repository.GetRawSignalPageAsync(runId, parsed, FluorescenceChannel.Rox, offset, pageSize, token).ConfigureAwait(false);
            var after = await repository.GetByRunIdAsync(runId, token).ConfigureAwait(false);
            if (after is null || before.RunRevision != after.RunRevision) return Failure<ExplorerPairPageDto>("SYSTEM_STALE_RUN_REVISION", "Run 已改变，请重新读取。");
            if (fam.TotalPoints != rox.TotalPoints || fam.Points.Count != rox.Points.Count) return Failure<ExplorerPairPageDto>("PLOT_EXPLORER_CHANNEL_MISMATCH", "FAM/ROX 原始记录不能按点配对。");
            var points = new ExplorerPairedPointDto[fam.Points.Count];
            for (int index = 0; index < points.Length; index++)
            {
                token.ThrowIfCancellationRequested();
                var left = fam.Points[index]; var right = rox.Points[index];
                if (left.PointIndex != right.PointIndex || left.ElapsedTicks != right.ElapsedTicks) return Failure<ExplorerPairPageDto>("PLOT_EXPLORER_CHANNEL_MISMATCH", "FAM/ROX 原始时间轴不一致。");
                points[index] = new ExplorerPairedPointDto(left.PointIndex, left.ElapsedTicks, left.Rfu, right.Rfu);
            }
            return GatewayResult.Ok(new ExplorerPairPageDto(runId, before.RunRevision, parsed.ToString(), source.Sha256, offset, fam.TotalPoints, points));
        }, token);

    public Task<GatewayResult<SavedExplorerViewDto>> SaveExplorerViewAsync(ExplorerViewDefinitionDto definition, CancellationToken token) =>
        ExecuteAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(definition);
            var run = await repository.GetByRunIdAsync(definition.RunId, token).ConfigureAwait(false);
            var source = await repository.GetSourceAsync(definition.RunId, token).ConfigureAwait(false);
            if (run is null || (definition.Mode == "raw" && source?.Sha256 != definition.SourceSha256)) return Failure<SavedExplorerViewDto>("PLOT_EXPLORER_SOURCE_CHANGED", "视图的原始来源与 Run 不一致。");
            byte[] content = JsonSerializer.SerializeToUtf8Bytes(definition, ExplorerJsonContext.Default.ExplorerViewDefinitionDto);
            string hash = Convert.ToHexStringLower(SHA256.HashData(content));
            var saved = new SavedExplorerViewDto(definition, hash);
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(saved, ExplorerJsonContext.Default.SavedExplorerViewDto);
            if (bytes.Length > MaximumExplorerFileBytes) return Failure<SavedExplorerViewDto>("PLOT_EXPLORER_VIEW_TOO_LARGE", "视图定义超过保存预算。");
            RelativePath folder = RelativePath.Parse($"explorer/{definition.RunId:N}");
            Directory.CreateDirectory(Resolve(folder));
            string destination = Resolve(folder.Combine("view.json"));
            string temporary = Resolve(folder.Combine($"view-{Guid.NewGuid():N}.tmp"));
            try
            {
                await File.WriteAllBytesAsync(temporary, bytes, token).ConfigureAwait(false);
                byte[] readback = await File.ReadAllBytesAsync(temporary, token).ConfigureAwait(false);
                if (!bytes.AsSpan().SequenceEqual(readback)) return Failure<SavedExplorerViewDto>("PLOT_EXPLORER_VIEW_WRITE_FAILED", "视图写入校验失败。");
                token.ThrowIfCancellationRequested();
                File.Move(temporary, destination, overwrite: true);
                return GatewayResult.Ok(saved);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }, token);

    public Task<GatewayResult<SavedExplorerViewDto?>> LoadExplorerViewAsync(Guid runId, CancellationToken token) =>
        ExecuteAsync(async () =>
        {
            if (await repository.GetByRunIdAsync(runId, token).ConfigureAwait(false) is null) return Failure<SavedExplorerViewDto?>("PROJECT_RUN_NOT_FOUND", "Run 不存在。");
            string path = Resolve(RelativePath.Parse($"explorer/{runId:N}/view.json"));
            if (!File.Exists(path)) return GatewayResult.Ok<SavedExplorerViewDto?>(null);
            if (new FileInfo(path).Length > MaximumExplorerFileBytes) return Failure<SavedExplorerViewDto?>("PLOT_EXPLORER_VIEW_TOO_LARGE", "保存的视图超过读取预算。");
            SavedExplorerViewDto saved;
            try
            {
                await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.Asynchronous);
                if (stream.Length > MaximumExplorerFileBytes) return Failure<SavedExplorerViewDto?>("PLOT_EXPLORER_VIEW_TOO_LARGE", "保存的视图超过读取预算。");
                byte[] bytes = new byte[checked((int)stream.Length)];
                await stream.ReadExactlyAsync(bytes, token).ConfigureAwait(false);
                saved = JsonSerializer.Deserialize(bytes, ExplorerJsonContext.Default.SavedExplorerViewDto) ?? throw new JsonException();
            }
            catch (Exception error) when (error is JsonException or ArgumentException) { return Failure<SavedExplorerViewDto?>("PLOT_EXPLORER_VIEW_INVALID", "保存的视图定义不兼容或损坏。"); }
            string hash = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(saved.Definition, ExplorerJsonContext.Default.ExplorerViewDefinitionDto)));
            var source = await repository.GetSourceAsync(runId, token).ConfigureAwait(false);
            if (saved.Definition.RunId != runId || hash != saved.Sha256 || (saved.Definition.Mode == "raw" && saved.Definition.SourceSha256 != source?.Sha256))
                return Failure<SavedExplorerViewDto?>("PLOT_EXPLORER_VIEW_IDENTITY_MISMATCH", "视图 hash 或原始来源不匹配。");
            return GatewayResult.Ok<SavedExplorerViewDto?>(saved);
        }, token);
}
