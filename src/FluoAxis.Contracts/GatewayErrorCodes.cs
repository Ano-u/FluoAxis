namespace FluoAxis.Contracts;

/// <summary>Stable error-code vocabulary exposed at the frontend-backend boundary.</summary>
public static class GatewayErrorCodes
{
    public const string UnsupportedContractVersion = Domain.ErrorCodes.SystemUnsupportedContractVersion;
    public const string RunNotFound = Domain.ErrorCodes.ProjectRunNotFound;
    public const string StaleRunRevision = Domain.ErrorCodes.SystemStaleRunRevision;
    public const string InvalidRunState = Domain.ErrorCodes.SystemInvalidRunState;
    public const string CapabilityUnavailable = Domain.ErrorCodes.SystemCapabilityUnavailable;
    public const string RequestIdConflict = Domain.ErrorCodes.SystemRequestIdConflict;
}
