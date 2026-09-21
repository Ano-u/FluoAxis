namespace FluoAxis.Domain;

/// <summary>
/// Central registry of the error codes defined by the Method-Specification
/// and Data-Contract documents. Error codes are a compatibility contract:
/// codes may not be renamed or invented casually. New codes require a
/// requirement-backed change.
/// </summary>
public static class ErrorCodes
{
    // Application orchestration (plan.md sections 5.2, 5.4, and 9.3)
    public const string SystemUnsupportedContractVersion = "SYSTEM_UNSUPPORTED_CONTRACT_VERSION";
    public const string ProjectRunNotFound = "PROJECT_RUN_NOT_FOUND";
    public const string SystemStaleRunRevision = "SYSTEM_STALE_RUN_REVISION";
    public const string SystemInvalidRunState = "SYSTEM_INVALID_RUN_STATE";
    public const string SystemCapabilityUnavailable = "SYSTEM_CAPABILITY_UNAVAILABLE";
    public const string SystemRequestIdConflict = "SYSTEM_REQUEST_ID_CONFLICT";

    // Import layer (Data-Contract §3: untrusted input, fail closed)
    public const string ImportDuplicateTime = "IMPORT_DUPLICATE_TIME";
    public const string ImportNonMonotonicTime = "IMPORT_NON_MONOTONIC_TIME";
    public const string ImportInvalidRfu = "IMPORT_INVALID_RFU";
    public const string ImportManagedResourceNotFound = "IMPORT_MANAGED_RESOURCE_NOT_FOUND";
    public const string ImportManagedResourceSizeMismatch = "IMPORT_MANAGED_RESOURCE_SIZE_MISMATCH";
    public const string ImportManagedResourceUnsafe = "IMPORT_MANAGED_RESOURCE_UNSAFE";
    public const string ImportSourceUnavailable = "IMPORT_SOURCE_UNAVAILABLE";
    public const string ImportSourceChanged = "IMPORT_SOURCE_CHANGED";
    public const string ImportStagingCollision = "IMPORT_STAGING_COLLISION";

    // Plate mapping (Method-Spec §10: role cardinality, fail closed)
    public const string PlateRoleCardinality = "PLATE_ROLE_CARDINALITY";

    // Calibration (Method-Spec §4/§5/§6/§7)
    public const string CalibrationMissingEndpoint = "CALIBRATION_MISSING_ENDPOINT";
    public const string CalibrationInsufficientWindowPoints = "CALIBRATION_INSUFFICIENT_WINDOW_POINTS";
    public const string CalibrationNonPositiveMean = "CALIBRATION_NON_POSITIVE_MEAN";
    public const string CalibrationInvalidDenominator = "CALIBRATION_INVALID_DENOMINATOR";
    public const string CalibrationNonFiniteValue = "CALIBRATION_NON_FINITE_VALUE";
    public const string CalibrationNtcRangeExceeded = "CALIBRATION_NTC_RANGE_EXCEEDED";

    // Controls (Method-Spec §8/§9.7)
    public const string ControlWtMeanExceeded = "CONTROL_WT_MEAN_EXCEEDED";
    public const string ControlLqcDirectionNegative = "CONTROL_LQC_DIRECTION_NEGATIVE";

    // Sample replicate consistency (Method-Spec §9.4)
    public const string SampleReplicateClassificationInconsistent = "SAMPLE_REPLICATE_CLASSIFICATION_INCONSISTENCY";
}
