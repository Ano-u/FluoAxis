namespace FluoAxis.Infrastructure;

using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluoAxis.Contracts;
using FluoAxis.Domain;

public sealed partial class PersistentDesktopStore
{
    private readonly Queue<DiagnosticEventDto> diagnosticEvents = new();
    private readonly Lock diagnosticGate = new();
    private bool recoveredUncleanSession;
    private bool sessionMarkerOwned;

    private void RecordDiagnostic(string code, SafeFailureCategory category)
    {
        lock (diagnosticGate)
        {
            while (diagnosticEvents.Count >= 100) diagnosticEvents.Dequeue();
            diagnosticEvents.Enqueue(new DiagnosticEventDto(DateTimeOffset.UtcNow, code, category.ToString(), Guid.NewGuid()));
        }
    }

    private async Task OpenSessionMarkerAsync()
    {
        string marker = Resolve(RelativePath.Parse("session.active"));
        recoveredUncleanSession = File.Exists(marker);
        await using (var connection = CreateConnection())
        {
            await connection.OpenAsync().ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA quick_check;";
            if (!string.Equals(await command.ExecuteScalarAsync().ConfigureAwait(false) as string, "ok", StringComparison.Ordinal))
                throw new InvalidOperationException("SYSTEM_WORKSPACE_INTEGRITY_FAILED");
        }
        await File.WriteAllTextAsync(marker, "FluoAxis session marker v1\n", new UTF8Encoding(false)).ConfigureAwait(false);
        sessionMarkerOwned = true;
        RecordDiagnostic(recoveredUncleanSession ? "WORKSPACE_RECOVERED" : "WORKSPACE_OPENED", SafeFailureCategory.None);
    }

    public Task<GatewayResult<WorkspaceDiagnosticsDto>> GetDiagnosticsAsync(CancellationToken cancellationToken) => ExecuteAsync(async () =>
    {
        var entries = await repository.ListProjectsAsync(0, 100, cancellationToken).ConfigureAwait(false);
        var next = entries.Count == 100 ? await repository.ListProjectsAsync(100, 1, cancellationToken).ConfigureAwait(false) : [];
        DiagnosticEventDto[] events;
        lock (diagnosticGate) events = diagnosticEvents.ToArray();
        return GatewayResult.Ok(new WorkspaceDiagnosticsDto(typeof(PersistentDesktopStore).Assembly.GetName().Version?.ToString() ?? "unknown",
            Environment.Version.ToString(), Environment.OSVersion.Version.ToString(), RuntimeInformation.ProcessArchitecture.ToString(),
            Environment.ProcessorCount, GC.GetTotalMemory(false), entries.Count, next.Count > 0, recoveredUncleanSession, Array.AsReadOnly(events)));
    }, cancellationToken);

    public async Task<GatewayResult<SupportBundleDto>> CreateSupportBundleAsync(CancellationToken cancellationToken)
    {
        GatewayResult<WorkspaceDiagnosticsDto> diagnostics = await GetDiagnosticsAsync(cancellationToken).ConfigureAwait(false);
        if (!diagnostics.IsSuccess) return GatewayResult.Fail<SupportBundleDto>(diagnostics.Errors.ToArray());
        return await ExecuteAsync(async () =>
        {
            byte[] json = JsonSerializer.SerializeToUtf8Bytes(diagnostics.Value!, DiagnosticsJsonContext.Default.WorkspaceDiagnosticsDto);
            if (json.Length > 64 * 1024) return Failure<SupportBundleDto>("SYSTEM_DIAGNOSTIC_LIMIT", "诊断数据超过限定大小。");
            string folder = Resolve(RelativePath.Parse("support"));
            Directory.CreateDirectory(folder);
            string identity = Guid.NewGuid().ToString("N");
            string relative = $"support/support-{identity}.zip";
            string destination = Resolve(RelativePath.Parse(relative));
            string temporary = Resolve(RelativePath.Parse($"support/build-{identity}.tmp"));
            try
            {
                await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 16384, FileOptions.Asynchronous))
                {
                    using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
                    {
                        await using (Stream entry = archive.CreateEntry("diagnostics.json").Open())
                            await entry.WriteAsync(json, cancellationToken).ConfigureAwait(false);
                        await using Stream notice = archive.CreateEntry("README.txt").Open();
                        await notice.WriteAsync(Encoding.UTF8.GetBytes("FluoAxis RUO technical support bundle. Contains allowlisted runtime metadata and at most 100 technical event codes. No raw RFU, source files, project names, source paths, traces, or exception text.\n"), cancellationToken).ConfigureAwait(false);
                    }
                    await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                    stream.Position = 0;
                    string hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
                    long length = stream.Length;
                    stream.Close();
                    cancellationToken.ThrowIfCancellationRequested();
                    File.Move(temporary, destination, false);
                    return GatewayResult.Ok(new SupportBundleDto(relative, hash, length));
                }
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }, cancellationToken).ConfigureAwait(false);
    }
}

[JsonSerializable(typeof(WorkspaceDiagnosticsDto))]
internal sealed partial class DiagnosticsJsonContext : JsonSerializerContext;
