using Platform.Api.Models.Identity;

namespace Platform.Api.Interfaces.Identity;

public interface IIdentityContext
{
    CallerIdentity? Current { get; }
}
