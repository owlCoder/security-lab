using Platform.Audit.Contracts;
using Platform.Membership.Domain;

namespace Platform.Membership.Application;

/// <summary>
/// ACC-02 / AUD-01: upis je promena prava, pa se beleži course.member.role.changed (none -> student).
/// Šema događaja je u katalogu Tima 12 (AuditEventCatalog).
/// </summary>
public static class MembershipAuditEvents
{
	public static AuditEventDraft Enrolled(CourseMember member)
	{
		ArgumentNullException.ThrowIfNull(member);
		return new AuditEventDraft(
			AuditEventTypes.CourseMemberRoleChanged,
			AuditOutcome.Success,
			CourseId: member.CourseId,
			Resource: new AuditResourceRef(AuditResourceTypes.Account, member.AccountId.ToString("D")),
			Details: new Dictionary<string, string>
			{
				["oldRole"] = "none",
				["newRole"] = RoleName(member.Role)
			});
	}

	public static string RoleName(MemberRole role) => role switch
	{
		MemberRole.Student => "student",
		MemberRole.Teacher => "teacher",
		MemberRole.Assistant => "assistant",
		_ => throw new ArgumentOutOfRangeException(nameof(role), role, "Unknown course role.")
	};
}