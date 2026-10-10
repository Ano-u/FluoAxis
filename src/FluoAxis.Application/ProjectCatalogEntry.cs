namespace FluoAxis.Application;

using FluoAxis.Domain;

/// <summary>
/// Path-free catalog row for one persisted project/Run pair. Listing a
/// catalog does not load the Run's signals, Method, or analysis evidence.
/// </summary>
public sealed record ProjectCatalogEntry(
    Guid ProjectId,
    string ProjectName,
    Guid RunId,
    int RunRevision,
    RunState RunState,
    DateTimeOffset CreatedAtUtc);
