namespace FluoAxis.Contracts;

/// <summary>
/// Version of the frontend-backend contract (plan.md §5). Breaking DTO
/// changes require bumping this version; the backend decides how to serve
/// older versions, so commands with a mismatched version stay constructible.
/// V3 makes <see cref="ConfirmMappingCommand"/> carry the staged source
/// identity, because the frozen Gateway has no separate commit-import
/// command and mapping confirmation is where a managed resource becomes the
/// Run's imported data.
/// </summary>
public static class ContractVersion
{
    public const int Current = 3;
}
