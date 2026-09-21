namespace FluoAxis.Import;

/// <summary>
/// Import-layer error codes. Extends the Domain <see cref="FluoAxis.Domain.ErrorCodes"/>
/// registry with the structural import failures defined by the Data-Contract
/// (format detection, limits, mapping mismatch, value parsing). Codes are a
/// compatibility contract: new codes require a requirement-backed change.
/// </summary>
public static class ImportErrorCodes
{
    public const string ImportFileTooLarge = "IMPORT_FILE_TOO_LARGE";
    public const string ImportTooManyRows = "IMPORT_TOO_MANY_ROWS";
    public const string ImportTooManyCells = "IMPORT_TOO_MANY_CELLS";
    public const string ImportTooManyColumns = "IMPORT_TOO_MANY_COLUMNS";
    public const string ImportSharedStringOverflow = "IMPORT_SHARED_STRING_OVERFLOW";
    public const string ImportFormatMismatch = "IMPORT_FORMAT_MISMATCH";
    public const string ImportWorkbookCorrupt = "IMPORT_WORKBOOK_CORRUPT";
    public const string ImportRegionNotFound = "IMPORT_REGION_NOT_FOUND";
    public const string ImportWellSetMismatch = "IMPORT_WELL_SET_MISMATCH";
    public const string ImportChannelTimeAxisMismatch = "IMPORT_CHANNEL_TIME_AXIS_MISMATCH";
    public const string ImportInvalidValue = "IMPORT_INVALID_VALUE";
    public const string ImportInvalidMapping = "IMPORT_INVALID_MAPPING";
    public const string ImportUnsupportedEncoding = "IMPORT_UNSUPPORTED_ENCODING";
}
