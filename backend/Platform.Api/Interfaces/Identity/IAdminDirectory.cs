namespace Platform.Api.Interfaces.Identity;

public interface IAdminDirectory
{
    bool IsAdministrator(Guid userId);
}
