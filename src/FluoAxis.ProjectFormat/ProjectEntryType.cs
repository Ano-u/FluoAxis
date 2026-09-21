namespace FluoAxis.ProjectFormat;

using FluoAxis.Domain;

/// <summary>
/// Kind of resource stored inside a project package. Drives access grouping
/// and validation strictness; mirrors the logical layout of CLAUDE.md §21.
/// </summary>
public enum ProjectEntryType
{
    /// <summary>Raw imported source file kept as a managed project resource.</summary>
    SourceRaw,

    /// <summary>Hash-only record of an imported source (content not retained).</summary>
    SourceHash,

    /// <summary>SQLite project database.</summary>
    Database,

    /// <summary>Serialized plot specification.</summary>
    PlotSpec,

    /// <summary>Generated report document.</summary>
    Report,

    /// <summary>Audit-trail record.</summary>
    Audit,
}
