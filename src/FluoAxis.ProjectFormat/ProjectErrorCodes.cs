namespace FluoAxis.ProjectFormat;

using FluoAxis.Domain;

/// <summary>
/// Structured error codes for the project-package layer (plan.md §7.4
/// defensive checklist). Codes are a compatibility contract: they follow the
/// PROJECT_ prefix rule of <see cref="AppError.CodePattern"/> and may not be
/// renamed casually. New codes require a requirement-backed change.
/// </summary>
public static class ProjectErrorCodes
{
    public const string NotAPackage = "PROJECT_NOT_A_PACKAGE";
    public const string EntryOutsideRoot = "PROJECT_ENTRY_OUTSIDE_ROOT";
    public const string EntryTraversal = "PROJECT_ENTRY_TRAVERSAL";
    public const string EntryAbsolutePath = "PROJECT_ENTRY_ABSOLUTE_PATH";
    public const string EntryNameInvalid = "PROJECT_ENTRY_NAME_INVALID";
    public const string DuplicateEntry = "PROJECT_DUPLICATE_ENTRY";
    public const string CaseCollision = "PROJECT_CASE_COLLISION";
    public const string CompressionRatio = "PROJECT_COMPRESSION_RATIO";
    public const string TotalSizeExceeded = "PROJECT_TOTAL_SIZE_EXCEEDED";
    public const string ManifestMissing = "PROJECT_MANIFEST_MISSING";
    public const string ManifestInvalid = "PROJECT_MANIFEST_INVALID";
    public const string EntryHashMismatch = "PROJECT_ENTRY_HASH_MISMATCH";
    public const string UnsupportedFormatVersion = "PROJECT_UNSUPPORTED_FORMAT_VERSION";
    public const string EntryNotDeclared = "PROJECT_ENTRY_NOT_DECLARED";
    public const string EntrySizeExceeded = "PROJECT_ENTRY_SIZE_EXCEEDED";
    public const string EntryCountExceeded = "PROJECT_ENTRY_COUNT_EXCEEDED";
    public const string SaveInvalidContents = "PROJECT_SAVE_INVALID_CONTENTS";
    public const string SaveFailed = "PROJECT_SAVE_FAILED";
    public const string ReplaceFailed = "PROJECT_REPLACE_FAILED";
}
