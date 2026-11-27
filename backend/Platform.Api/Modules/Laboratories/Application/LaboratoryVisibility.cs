using Platform.Api.Modules.Laboratories.Domain;

namespace Platform.Api.Modules.Laboratories.Application;

public static class LaboratoryVisibility
{
    public static bool IsVisibleTo(Laboratory? laboratory, Guid callerId) =>
        laboratory is not null && callerId != Guid.Empty && laboratory.CreatedBy == callerId;
}
