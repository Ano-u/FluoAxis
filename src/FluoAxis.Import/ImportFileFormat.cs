namespace FluoAxis.Import;

/// <summary>
/// File format detected during import inspection. Legacy XLS is out of
/// scope for this adapter generation (Data-Contract §1.2).
/// </summary>
public enum ImportFileFormat
{
    Unknown,
    Xlsx,
    DelimitedText,
}
