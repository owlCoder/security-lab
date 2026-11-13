using Platform.LabVisibility.Contracts;
using Platform.LabVisibility.Domain;

namespace Platform.Api.Modules.Laboratories.Application;

public static class CatalogVisibilityPolicy
{
    public static bool CanView(
        LabPublicContent content,
        CourseRole role)
    {
        if (role == CourseRole.Teacher)
            return true;

        return role == CourseRole.Student &&
               content.State == LabVisibilityState.Published;
    }
}