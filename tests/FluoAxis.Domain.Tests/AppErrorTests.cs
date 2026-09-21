namespace FluoAxis.Domain.Tests;

using Xunit;

public class AppErrorTests
{
    [Fact]
    public void Constructor_accepts_every_registered_code()
    {
        string[] codes =
        [
            ErrorCodes.SystemUnsupportedContractVersion,
            ErrorCodes.ProjectRunNotFound,
            ErrorCodes.SystemStaleRunRevision,
            ErrorCodes.SystemInvalidRunState,
            ErrorCodes.SystemCapabilityUnavailable,
            ErrorCodes.SystemRequestIdConflict,
            ErrorCodes.ImportDuplicateTime,
            ErrorCodes.ImportNonMonotonicTime,
            ErrorCodes.ImportInvalidRfu,
            ErrorCodes.ImportManagedResourceNotFound,
            ErrorCodes.ImportManagedResourceSizeMismatch,
            ErrorCodes.ImportManagedResourceUnsafe,
            ErrorCodes.ImportSourceUnavailable,
            ErrorCodes.ImportSourceChanged,
            ErrorCodes.ImportStagingCollision,
            ErrorCodes.PlateRoleCardinality,
            ErrorCodes.CalibrationMissingEndpoint,
            ErrorCodes.CalibrationInsufficientWindowPoints,
            ErrorCodes.CalibrationNonPositiveMean,
            ErrorCodes.CalibrationInvalidDenominator,
            ErrorCodes.CalibrationNonFiniteValue,
            ErrorCodes.CalibrationNtcRangeExceeded,
            ErrorCodes.ControlWtMeanExceeded,
            ErrorCodes.ControlLqcDirectionNegative,
            ErrorCodes.SampleReplicateClassificationInconsistent,
        ];

        Assert.All(codes, code =>
        {
            var error = new AppError(code, ErrorSeverity.Blocking, "message");
            Assert.Equal(code, error.Code);
        });
    }

    [Theory]
    [InlineData("CALIBRATION_MISSING_ENDPOINT")]
    [InlineData("IMPORT_DUPLICATE_TIME")]
    [InlineData("SYSTEM_UNEXPECTED")]
    [InlineData("PLOT_RENDER_FAILED_2")]
    [InlineData("CONTROL_LQC_DIRECTION_NEGATIVE")]
    public void Constructor_accepts_well_formed_codes(string code)
    {
        var error = new AppError(code, ErrorSeverity.Warning, "user message");

        Assert.Equal(code, error.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("calibration_missing_endpoint")]
    [InlineData("SCIENCE_MISSING_ENDPOINT")]
    [InlineData("CALIBRATION-MISSING")]
    [InlineData("CALIBRATION_")]
    [InlineData("_CALIBRATION_X")]
    [InlineData("CALIBRATION_MISSING ENDPOINT")]
    public void Constructor_rejects_malformed_codes(string code)
    {
        Assert.Throws<ArgumentException>(() => new AppError(code, ErrorSeverity.Info, "message"));
    }

    [Fact]
    public void Constructor_rejects_blank_code_or_message()
    {
        Assert.ThrowsAny<ArgumentException>(() => new AppError("", ErrorSeverity.Info, "message"));
        Assert.ThrowsAny<ArgumentException>(() => new AppError("SYSTEM_X", ErrorSeverity.Info, ""));
        Assert.ThrowsAny<ArgumentException>(() => new AppError("SYSTEM_X", ErrorSeverity.Info, null!));
    }

    [Fact]
    public void Optional_fields_round_trip()
    {
        var runId = Guid.NewGuid();

        var error = new AppError(ErrorCodes.CalibrationMissingEndpoint, ErrorSeverity.Blocking, "endpoint missing", "SAFE_DETAIL_1", runId);

        Assert.Equal("SAFE_DETAIL_1", error.SafeDetailCode);
        Assert.Equal(runId, error.RunId);
        Assert.Null(new AppError("SYSTEM_X", ErrorSeverity.Info, "message").SafeDetailCode);
    }
}
