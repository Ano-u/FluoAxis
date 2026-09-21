namespace FluoAxis.Infrastructure.Tests;

using System.IO.Compression;
using System.Text;
using FluoAxis.Application;
using FluoAxis.Contracts;
using FluoAxis.Domain;
using FluoAxis.Import;
using FluoAxis.Infrastructure;
using Xunit;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class FilesystemBoundaryGroup
{
    public const string Name = "managed-import-filesystem-boundary";
}

[Collection(FilesystemBoundaryGroup.Name)]
public sealed class ManagedImportInspectionAdapterTests
{
    [Fact]
    public async Task Inspect_csv_returns_candidate_dto_and_progress_without_mutating_content()
    {
        using TestWorkspace workspace = new();
        byte[] content = Encoding.UTF8.GetBytes("Time,A1,A2\r\n0,100,200\r\n2,101,201");
        workspace.Write("data/raw/run.csv", content);
        var progress = new ProgressRecorder();
        var adapter = new ManagedImportInspectionAdapter(workspace.Root);

        Result<ImportPreviewDto> result = await adapter.InspectAsync(
            Request("run.csv", content.Length, "data/raw/run.csv", "csv"),
            progress,
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, ErrorText(result));
        Assert.Equal("delimitedText", result.Value!.DetectedFormat);
        RegionCandidateDto candidate = Assert.Single(result.Value.CandidateRegions);
        Assert.Equal("text-body", candidate.SheetName);
        Assert.Equal(0, candidate.HeaderRowIndex);
        Assert.Equal(0, candidate.TimeColumnIndex);
        Assert.Equal(["A1", "A2"], candidate.WellColumns.Select(item => item.Well));
        Assert.Equal(
            [ImportStageNames.DetectingFormat, ImportStageNames.DiscoveringRegion, ImportStageNames.Validating, ImportStageNames.Validating],
            progress.Items.Select(item => item.Stage));
        Assert.Equal(content, workspace.Read("data/raw/run.csv"));
    }

    [Fact]
    public async Task Inspect_real_xlsx_detects_signature_and_sheet()
    {
        using TestWorkspace workspace = new();
        byte[] workbook = BuildWorkbook();
        workspace.Write("data/raw/run.xlsx", workbook);
        var adapter = new ManagedImportInspectionAdapter(workspace.Root);

        Result<ImportPreviewDto> result = await adapter.InspectAsync(
            Request("run.xlsx", workbook.Length, "data/raw/run.xlsx", "xlsx"),
            null,
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, ErrorText(result));
        Assert.Equal("xlsx", result.Value!.DetectedFormat);
        Assert.Equal(["FAM"], result.Value.SheetNames);
        Assert.Equal(ChannelNames.Fam, Assert.Single(result.Value.CandidateRegions).SuggestedChannel);
    }

    [Fact]
    public async Task Inspect_rejects_missing_changed_oversized_and_mismatched_resources_without_path_leak()
    {
        using TestWorkspace workspace = new();
        workspace.Write("data/raw/run.csv", "Time,A1\r\n0,1"u8.ToArray());
        var adapter = new ManagedImportInspectionAdapter(workspace.Root, new ImportLimits(maxBytes: 8));

        Result<ImportPreviewDto> missing = await adapter.InspectAsync(
            Request("missing.csv", 4, "data/raw/missing.csv"), null, TestContext.Current.CancellationToken);
        Result<ImportPreviewDto> changed = await adapter.InspectAsync(
            Request("run.csv", 99, "data/raw/run.csv"), null, TestContext.Current.CancellationToken);
        Result<ImportPreviewDto> oversized = await adapter.InspectAsync(
            Request("run.csv", workspace.Length("data/raw/run.csv"), "data/raw/run.csv"), null, TestContext.Current.CancellationToken);
        Result<ImportPreviewDto> mismatch = await new ManagedImportInspectionAdapter(workspace.Root).InspectAsync(
            Request("run.xlsx", workspace.Length("data/raw/run.csv"), "data/raw/run.csv", "xlsx"), null, TestContext.Current.CancellationToken);
        workspace.Write("data/raw/binary.csv", [1, 0, 2, 0, 3]);
        Result<ImportPreviewDto> binary = await new ManagedImportInspectionAdapter(workspace.Root).InspectAsync(
            Request("binary.csv", 5, "data/raw/binary.csv", "csv"), null, TestContext.Current.CancellationToken);
        workspace.Write("data/raw/invalid-utf8.csv", [0xc3, 0x28]);
        Result<ImportPreviewDto> invalidUtf8 = await new ManagedImportInspectionAdapter(workspace.Root).InspectAsync(
            Request("invalid-utf8.csv", 2, "data/raw/invalid-utf8.csv", "csv"), null, TestContext.Current.CancellationToken);

        AssertFailure(missing, ErrorCodes.ImportManagedResourceNotFound, workspace.Root);
        AssertFailure(changed, ErrorCodes.ImportManagedResourceSizeMismatch, workspace.Root);
        AssertFailure(oversized, ImportErrorCodes.ImportFileTooLarge, workspace.Root);
        AssertFailure(mismatch, ImportErrorCodes.ImportFormatMismatch, workspace.Root);
        AssertFailure(binary, ImportErrorCodes.ImportFormatMismatch, workspace.Root);
        AssertFailure(invalidUtf8, ImportErrorCodes.ImportFormatMismatch, workspace.Root);
    }

    [Fact]
    public async Task Inspect_is_cwd_independent_and_disposes_stream_after_importer_returns()
    {
        using TestWorkspace workspace = new();
        workspace.Write("data/raw/run.csv", "x"u8.ToArray());
        var importer = new CapturingImporter();
        var adapter = new ManagedImportInspectionAdapter(workspace.Root, delimitedImporter: importer);
        string originalCwd = Environment.CurrentDirectory;
        string foreignCwd = workspace.CreateDirectory("foreign-cwd");

        try
        {
            Environment.CurrentDirectory = foreignCwd;
            Result<ImportPreviewDto> result = await adapter.InspectAsync(
                Request("run.csv", 1, "data/raw/run.csv"), null, TestContext.Current.CancellationToken);
            Assert.True(result.IsSuccess, ErrorText(result));
        }
        finally
        {
            Environment.CurrentDirectory = originalCwd;
        }

        Assert.NotNull(importer.CapturedStream);
        Assert.False(importer.CapturedStream.CanRead);
    }

    [Fact]
    public async Task Inspect_honors_pre_cancellation_before_opening_resource()
    {
        using TestWorkspace workspace = new();
        workspace.Write("data/raw/run.csv", "x"u8.ToArray());
        var importer = new CapturingImporter();
        var adapter = new ManagedImportInspectionAdapter(workspace.Root, delimitedImporter: importer);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => adapter.InspectAsync(
            Request("run.csv", 1, "data/raw/run.csv"), null, cancellation.Token));

        Assert.Null(importer.CapturedStream);
    }

    [Fact]
    public void Request_and_relative_path_reject_external_or_traversing_identity()
    {
        Assert.Throws<FormatException>(() => RelativePath.Parse("../outside.csv"));
        Assert.Throws<FormatException>(() => RelativePath.Parse("/outside.csv"));
        Assert.Throws<ArgumentException>(() =>
            new ManagedImportRequest(Guid.NewGuid(), "folder/run.csv", 1, RelativePath.Parse("data/run.csv"), null));
    }

    private static ManagedImportRequest Request(string name, long length, string path, string? hint = null) =>
        new(Guid.NewGuid(), name, length, RelativePath.Parse(path), hint);

    private static void AssertFailure(Result<ImportPreviewDto> result, string code, string forbiddenRoot)
    {
        Assert.False(result.IsSuccess);
        AppError error = Assert.Single(result.Errors);
        Assert.Equal(code, error.Code);
        Assert.DoesNotContain(forbiddenRoot, error.UserMessage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(forbiddenRoot, error.SafeDetailCode ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    private static string ErrorText(Result<ImportPreviewDto> result) =>
        string.Join("; ", result.Errors.Select(error => $"{error.Code}: {error.UserMessage}"));

    private static byte[] BuildWorkbook()
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(archive, "[Content_Types].xml", """
                <?xml version="1.0" encoding="UTF-8"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
                  <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
                </Types>
                """);
            WriteEntry(archive, "_rels/.rels", """
                <?xml version="1.0" encoding="UTF-8"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
                </Relationships>
                """);
            WriteEntry(archive, "xl/workbook.xml", """
                <?xml version="1.0" encoding="UTF-8"?>
                <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <sheets><sheet name="FAM" sheetId="1" r:id="rId1"/></sheets>
                </workbook>
                """);
            WriteEntry(archive, "xl/_rels/workbook.xml.rels", """
                <?xml version="1.0" encoding="UTF-8"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
                </Relationships>
                """);
            WriteEntry(archive, "xl/worksheets/sheet1.xml", """
                <?xml version="1.0" encoding="UTF-8"?>
                <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData>
                  <row r="1"><c r="A1" t="inlineStr"><is><t>Time</t></is></c><c r="B1" t="inlineStr"><is><t>A1</t></is></c></row>
                  <row r="2"><c r="A2"><v>0</v></c><c r="B2"><v>100</v></c></row>
                  <row r="3"><c r="A3"><v>2</v></c><c r="B3"><v>101</v></c></row>
                </sheetData></worksheet>
                """);
        }

        return buffer.ToArray();
    }

    private static void WriteEntry(ZipArchive archive, string name, string content)
    {
        ZipArchiveEntry entry = archive.CreateEntry(name);
        using Stream stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        writer.Write(content);
    }

    private sealed class CapturingImporter : IImporter
    {
        public Stream? CapturedStream { get; private set; }

        public Task<Result<ImportPreview>> InspectAsync(Stream stream, ImportLimits limits, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CapturedStream = stream;
            return Task.FromResult(Result<ImportPreview>.Ok(new ImportPreview(
                ImportFileFormat.DelimitedText,
                [new RegionCandidate("text-body", null, null, new Dictionary<int, WellId>(), null, 0)],
                0, 0, [])));
        }

        public Task<Result<ImportedDataset>> ReadAsync(
            Stream stream, DataMapping mapping, ImportLimits limits, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class ProgressRecorder : IProgress<OperationProgressDto>
    {
        public List<OperationProgressDto> Items { get; } = [];
        public void Report(OperationProgressDto value) => Items.Add(value);
    }

    private sealed class TestWorkspace : IDisposable
    {
        private readonly string repositoryRoot = FindRepositoryRoot();
        public string Root { get; }

        public TestWorkspace()
        {
            Root = Path.Combine(repositoryRoot, "artifacts", "test-tmp", nameof(FluoAxis.Infrastructure.Tests),
                Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                nameof(ManagedImportInspectionAdapterTests), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public void Write(string relativePath, byte[] bytes)
        {
            string path = Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, bytes);
        }

        public byte[] Read(string relativePath) => File.ReadAllBytes(Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        public long Length(string relativePath) => new FileInfo(Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar))).Length;
        public string CreateDirectory(string relativePath) => Directory.CreateDirectory(Path.Combine(Root, relativePath)).FullName;

        public void Dispose()
        {
            string approved = Path.GetFullPath(Path.Combine(repositoryRoot, "artifacts", "test-tmp"))
                .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string resolved = Path.GetFullPath(Root);
            if (!resolved.StartsWith(approved, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Refusing to remove an Infrastructure fixture outside the approved test root.");
            }

            if (Directory.Exists(resolved))
            {
                Directory.Delete(resolved, recursive: true);
            }
        }

        private static string FindRepositoryRoot()
        {
            DirectoryInfo? current = new(AppContext.BaseDirectory);
            while (current is not null && !File.Exists(Path.Combine(current.FullName, "FluoAxis.slnx")))
            {
                current = current.Parent;
            }

            return current?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
        }
    }
}
