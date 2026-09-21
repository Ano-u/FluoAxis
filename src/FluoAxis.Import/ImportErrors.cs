namespace FluoAxis.Import;

using FluoAxis.Domain;

/// <summary>
/// Constructors for the structured blocking errors of the import layer.
/// User messages stay free of machine paths (P0 invariant); structured
/// positions live in <see cref="AppError.SafeDetailCode"/>.
/// </summary>
internal static class ImportErrors
{
    public static AppError FileTooLarge(long maxBytes) => new(
        ImportErrorCodes.ImportFileTooLarge,
        ErrorSeverity.Blocking,
        $"The file exceeds the import size limit of {maxBytes} bytes.",
        $"maxBytes={maxBytes}");

    public static AppError TooManyRows(long maxRows) => new(
        ImportErrorCodes.ImportTooManyRows,
        ErrorSeverity.Blocking,
        $"The file exceeds the import row limit of {maxRows} rows.",
        $"maxRows={maxRows}");

    public static AppError TooManyCells(long maxCells) => new(
        ImportErrorCodes.ImportTooManyCells,
        ErrorSeverity.Blocking,
        $"The file exceeds the import cell limit of {maxCells} cells.",
        $"maxCells={maxCells}");

    public static AppError TooManyColumns(int maxColumns) => new(
        ImportErrorCodes.ImportTooManyColumns,
        ErrorSeverity.Blocking,
        $"A row exceeds the import column limit of {maxColumns} columns.",
        $"maxColumns={maxColumns}");

    public static AppError SharedStringOverflow(int maxEntries) => new(
        ImportErrorCodes.ImportSharedStringOverflow,
        ErrorSeverity.Blocking,
        $"The workbook shared-string table exceeds the import limit of {maxEntries} entries.",
        $"maxEntries={maxEntries}");

    public static AppError FormatMismatch(string expected) => new(
        ImportErrorCodes.ImportFormatMismatch,
        ErrorSeverity.Blocking,
        "The file content does not match the expected format.",
        $"expected={expected}");

    public static AppError WorkbookCorrupt(string detail) => new(
        ImportErrorCodes.ImportWorkbookCorrupt,
        ErrorSeverity.Blocking,
        "The workbook structure is damaged or cannot be read.",
        detail);

    public static AppError RegionNotFound(string sheetName) => new(
        ImportErrorCodes.ImportRegionNotFound,
        ErrorSeverity.Blocking,
        "No readable table region was found in the file.",
        $"sheet={sheetName}");

    public static AppError WellSetMismatch(string missingInFam, string missingInRox) => new(
        ImportErrorCodes.ImportWellSetMismatch,
        ErrorSeverity.Blocking,
        "The FAM and ROX mappings do not cover the same set of wells.",
        $"missingInFam={missingInFam};missingInRox={missingInRox}");

    public static AppError ChannelTimeAxisMismatch() => new(
        ImportErrorCodes.ImportChannelTimeAxisMismatch,
        ErrorSeverity.Blocking,
        "The FAM and ROX channels do not share the same time axis.",
        "elapsedSequencesDiffer");

    public static AppError InvalidValue(int rowNumber, int columnIndex, string field) => new(
        ImportErrorCodes.ImportInvalidValue,
        ErrorSeverity.Blocking,
        $"A required value is missing or not numeric at row {rowNumber}, column {columnIndex + 1}.",
        $"row={rowNumber};col={columnIndex};field={field}");

    public static AppError InvalidNonFiniteRfu(string well, int rowNumber) => new(
        ErrorCodes.ImportInvalidRfu,
        ErrorSeverity.Blocking,
        $"Well {well} has a non-finite value at row {rowNumber}; NaN and Infinity are rejected.",
        $"row={rowNumber};well={well}");

    public static AppError InvalidTimeValue(int rowNumber, int columnIndex) => new(
        ImportErrorCodes.ImportInvalidValue,
        ErrorSeverity.Blocking,
        $"The time column value at row {rowNumber} is missing or not numeric.",
        $"row={rowNumber};col={columnIndex};field=time");

    public static AppError InvalidMapping(string detail) => new(
        ImportErrorCodes.ImportInvalidMapping,
        ErrorSeverity.Blocking,
        "The confirmed mapping does not match the file content.",
        detail);

    public static AppError DuplicateTime(int rowNumber) => new(
        ErrorCodes.ImportDuplicateTime,
        ErrorSeverity.Blocking,
        $"Row {rowNumber} repeats the elapsed time of the previous row; duplicate timestamps are rejected.",
        $"row={rowNumber}");

    public static AppError NonMonotonicTime(int rowNumber) => new(
        ErrorCodes.ImportNonMonotonicTime,
        ErrorSeverity.Blocking,
        $"Row {rowNumber} moves the elapsed time backwards; time must be strictly ascending.",
        $"row={rowNumber}");

    public static AppError RaggedRow(int rowNumber, int expectedColumns, int actualColumns) => new(
        ImportErrorCodes.ImportInvalidValue,
        ErrorSeverity.Warning,
        $"Row {rowNumber} has {actualColumns} fields instead of {expectedColumns}.",
        $"row={rowNumber};expectedCols={expectedColumns};actualCols={actualColumns}");

}
