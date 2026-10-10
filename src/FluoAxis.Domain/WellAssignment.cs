namespace FluoAxis.Domain;

/// <summary>
/// Role assignment of one well in the plate map. Only <see cref="WellRole.Sample"/>
/// wells may carry <see cref="Sample"/> metadata; every control role must
/// have a null sample (the roles are mutually exclusive per requirements
/// doc FR-PLATE and Method-Spec §10.3).
/// </summary>
public sealed record WellAssignment
{
    public WellId Well { get; }
    public WellRole Role { get; }
    public SampleAssignment? Sample { get; }

    public WellAssignment(WellId well, WellRole role, SampleAssignment? sample)
    {
        if (role == WellRole.Sample && sample is null)
        {
            throw new ArgumentException("Sample wells require sample metadata.", nameof(sample));
        }

        if (role != WellRole.Sample && sample is not null)
        {
            throw new ArgumentException($"Only {nameof(WellRole.Sample)} wells may carry sample metadata; role '{role}' must have a null sample.", nameof(sample));
        }

        Well = well;
        Role = role;
        Sample = sample;
    }
}
