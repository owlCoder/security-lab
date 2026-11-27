using Platform.Access.Domain.Enums;
using SharedReader = Platform.Access.Contracts.IMembershipReader;

namespace Platform.Api.Modules.Access.Integration;

/// <summary>Consumes ACC-02's published shared reader. Empty/missing provider never grants membership.</summary>
public sealed class SharedMembershipAdapter(IServiceScopeFactory scopes) : IMembershipReader
{
    public async ValueTask<PortResult<MembershipSnapshot>> FindMembershipAsync(Guid actorId, Guid courseId, CancellationToken ct = default)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var m = await scope.ServiceProvider.GetRequiredService<SharedReader>().GetAsync(actorId, courseId, ct);
            if (m is null) return PortResult<MembershipSnapshot>.Missing();
            if (m.AccountId != actorId || m.CourseId != courseId) return PortResult<MembershipSnapshot>.Offline();
            CourseRole? role = m.Role switch
            {
                Platform.Access.Domain.Enums.CourseRole.Student => CourseRole.Student,
                Platform.Access.Domain.Enums.CourseRole.Teacher => CourseRole.Teacher,
                Platform.Access.Domain.Enums.CourseRole.Assistant => CourseRole.Assistant,
                _ => null
            };
            return role is null ? PortResult<MembershipSnapshot>.Missing() :
                PortResult<MembershipSnapshot>.Found(new(actorId, courseId, role.Value, m.Status == MembershipStatus.Active));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception)
        { return PortResult<MembershipSnapshot>.Offline(); }
    }
}
