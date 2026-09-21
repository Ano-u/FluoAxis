namespace FluoAxis.Domain;

/// <summary>
/// Scientific role a well plays in the guided BRAF V600E workflow.
/// </summary>
public enum WellRole
{
    /// <summary>No-template blank calibration well; source of Fb.</summary>
    Ntc,

    /// <summary>100% positive calibration well; source of Fm.</summary>
    Positive100,

    /// <summary>Wild-type DNA negative control; dr' reference coordinate.</summary>
    WildType,

    /// <summary>0.1% low-quality control; expected to classify Positive.</summary>
    Lqc01Percent,

    /// <summary>Unknown specimen replicate; classified via dr'.</summary>
    Sample,

    /// <summary>Well excluded from the guided workflow.</summary>
    Ignore,
}
