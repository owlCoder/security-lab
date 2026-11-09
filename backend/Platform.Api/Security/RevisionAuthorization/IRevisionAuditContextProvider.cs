using Platform.Audit.Contracts;

namespace Platform.Api.Security.RevisionAuthorization;

public interface IRevisionAuditContextProvider
{
    AuditContext GetContext();
}
