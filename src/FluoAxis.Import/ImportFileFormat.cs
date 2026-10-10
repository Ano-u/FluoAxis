namespace FluoAxis.Import;

/// <summary>
/// File format detected during import inspection, including read-only legacy BIFF/OLE XLS.
/// </summary>
public enum ImportFileFormat
{
    Unknown,
    Xlsx,
    DelimitedText,
    Xls,
}
