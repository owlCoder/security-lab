using System.Collections.Frozen;
using Platform.Access.Domain.Constants;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Access.Infrastructure.Membership;
using Platform.Access.Infrastructure.Resources;

namespace Platform.Access.Infrastructure.Development;


public static class AccessDevelopmentSeed
{
    public static readonly Guid CourseA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    public static readonly Guid CourseB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    public static readonly Guid TeacherA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid StudentA = Guid.Parse("22222222-2222-2222-2222-222222222222");
    public static readonly Guid StudentB = Guid.Parse("33333333-3333-3333-3333-333333333333");

    public static readonly Guid TeacherB = Guid.Parse("66666666-6666-6666-6666-666666666666");
    public static readonly Guid AssistantA = Guid.Parse("77777777-7777-7777-7777-777777777777");
    public static readonly Guid FormerStudentA = Guid.Parse("88888888-8888-8888-8888-888888888888");

    public static readonly ResourceRef LabA = new("laboratory", "44444444-4444-4444-4444-444444444444");
    public static readonly ResourceRef LabArchivedA = new("laboratory", "44444444-4444-4444-4444-4444444444a1");
    public static readonly ResourceRef LabB = new("laboratory", "99999999-9999-9999-9999-999999999999");

    public static readonly ResourceRef AssignmentA = new("assignment", "10000000-0000-0000-0000-0000000000a1");
    public static readonly ResourceRef AssignmentExpiredA = new("assignment", "10000000-0000-0000-0000-0000000000a2");
    public static readonly ResourceRef AssignmentIncompleteA = new("assignment", "10000000-0000-0000-0000-0000000000a3");
    public static readonly ResourceRef AssignmentB = new("assignment", "10000000-0000-0000-0000-0000000000b1");

    public static readonly ResourceRef AttemptA = new("attempt", "20000000-0000-0000-0000-0000000000a1");
    public static readonly ResourceRef AttemptSubmittedA = new("attempt", "20000000-0000-0000-0000-0000000000a2");
    public static readonly ResourceRef AttemptB = new("attempt", "20000000-0000-0000-0000-0000000000b1");
    public static readonly ResourceRef AttemptOfFormerStudentA = new("attempt", "20000000-0000-0000-0000-0000000000c1");

    private static readonly DateTimeOffset FutureDue = new(2099, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset PastDue = new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static IReadOnlyList<ResourceRef> AllResources =>
        CreateDescriptors().Select(descriptor => new ResourceRef(descriptor.Type, descriptor.Id)).ToList();

    public static void Populate(InMemoryMembershipReader memberships, InMemoryResourceDescriptorResolver resources)
    {
        ArgumentNullException.ThrowIfNull(memberships);
        ArgumentNullException.ThrowIfNull(resources);

        memberships.Set(Member(TeacherA, CourseA, CourseRole.Teacher));
        memberships.Set(Member(StudentA, CourseA, CourseRole.Student));
        memberships.Set(Member(FormerStudentA, CourseA, CourseRole.Student, MembershipStatus.Left));
        memberships.Set(new CourseMembership(
            AssistantA,
            CourseA,
            CourseRole.Assistant,
            MembershipStatus.Active,
            new HashSet<string>(StringComparer.Ordinal) { AccessActions.AttemptReadCourse.Id }));

        memberships.Set(Member(StudentB, CourseB, CourseRole.Student));
        memberships.Set(Member(TeacherB, CourseB, CourseRole.Teacher));

        foreach (var descriptor in CreateDescriptors())
        {
            var resource = new ResourceRef(descriptor.Type, descriptor.Id);

            resources.SetCourse(
                resource,
                descriptor.CourseId ?? throw new InvalidOperationException(
                    $"Seed resource {descriptor.Type}:{descriptor.Id} has no course."));

            if (descriptor.OwnerId is { } ownerId)
            {
                resources.SetOwner(resource, ownerId);
            }
        }
    }

    public static IReadOnlyList<InMemoryResourceAttributeResolver> CreateAttributeResolvers()
    {
        var resolvers = new List<InMemoryResourceAttributeResolver>();

        foreach (var group in CreateDescriptors().GroupBy(descriptor => descriptor.Type, StringComparer.Ordinal))
        {
            var resolver = new InMemoryResourceAttributeResolver(group.Key);
            foreach (var descriptor in group)
            {
                resolver.Set(descriptor);
            }

            resolvers.Add(resolver);
        }

        return resolvers;
    }

    public static IReadOnlyList<ResourceDescriptor> CreateDescriptors() =>
    [
        Descriptor(LabA, CourseA, null, ResourceAttributeKeys.LaboratoryStatuses.Draft,
            Text(ResourceAttributeKeys.CourseStatus, ResourceAttributeKeys.CourseStatuses.Active)),
        Descriptor(LabArchivedA, CourseA, null, ResourceAttributeKeys.LaboratoryStatuses.Archived,
            Text(ResourceAttributeKeys.CourseStatus, ResourceAttributeKeys.CourseStatuses.Active)),
        Descriptor(LabB, CourseB, null, ResourceAttributeKeys.LaboratoryStatuses.Draft,
            Text(ResourceAttributeKeys.CourseStatus, ResourceAttributeKeys.CourseStatuses.Active)),

        AssignmentDescriptor(AssignmentA, CourseA, StudentA,
            ResourceAttributeKeys.AssignmentAvailabilities.Available, FutureDue, maxAttempts: 3, attemptsUsed: 1),
        AssignmentDescriptor(AssignmentExpiredA, CourseA, StudentA,
            ResourceAttributeKeys.AssignmentAvailabilities.Expired, PastDue, maxAttempts: 3, attemptsUsed: 1),
        AssignmentDescriptor(AssignmentB, CourseB, StudentB,
            ResourceAttributeKeys.AssignmentAvailabilities.Available, FutureDue, maxAttempts: 3, attemptsUsed: 0),

        Descriptor(AssignmentIncompleteA, CourseA, StudentA, ResourceAttributeKeys.AssignmentStatuses.Active,
            Text(ResourceAttributeKeys.CourseStatus, ResourceAttributeKeys.CourseStatuses.Active),
            Missing(ResourceAttributeKeys.Availability),
            Missing(ResourceAttributeKeys.DueAtUtc),
            Missing(ResourceAttributeKeys.MaxAttempts),
            Missing(ResourceAttributeKeys.AttemptsUsed)),

        AttemptDescriptor(AttemptA, CourseA, StudentA, ResourceAttributeKeys.AttemptStatuses.InProgress),
        AttemptDescriptor(AttemptSubmittedA, CourseA, StudentA, ResourceAttributeKeys.AttemptStatuses.Submitted),
        AttemptDescriptor(AttemptB, CourseB, StudentB, ResourceAttributeKeys.AttemptStatuses.InProgress),
        AttemptDescriptor(AttemptOfFormerStudentA, CourseA, FormerStudentA,
            ResourceAttributeKeys.AttemptStatuses.InProgress)
    ];

    private static ResourceDescriptor AssignmentDescriptor(
        ResourceRef resource,
        Guid courseId,
        Guid ownerId,
        string availability,
        DateTimeOffset dueAtUtc,
        int maxAttempts,
        int attemptsUsed) =>
        Descriptor(resource, courseId, ownerId, ResourceAttributeKeys.AssignmentStatuses.Active,
            Text(ResourceAttributeKeys.CourseStatus, ResourceAttributeKeys.CourseStatuses.Active),
            Text(ResourceAttributeKeys.LaboratoryStatus, ResourceAttributeKeys.LaboratoryStatuses.Draft),
            Text(ResourceAttributeKeys.Availability, availability),
            (ResourceAttributeKeys.DueAtUtc, AttributeValue.Of(dueAtUtc)),
            (ResourceAttributeKeys.AllowStartAfterDeadline, AttributeValue.Of(false)),
            (ResourceAttributeKeys.MaxAttempts, AttributeValue.Of(maxAttempts)),
            (ResourceAttributeKeys.AttemptsUsed, AttributeValue.Of(attemptsUsed)));

    private static ResourceDescriptor AttemptDescriptor(
        ResourceRef resource,
        Guid courseId,
        Guid ownerId,
        string status) =>
        Descriptor(resource, courseId, ownerId, status,
            Text(ResourceAttributeKeys.CourseStatus, ResourceAttributeKeys.CourseStatuses.Active));

    private static ResourceDescriptor Descriptor(
        ResourceRef resource,
        Guid courseId,
        Guid? ownerId,
        string status,
        params (string Key, AttributeValue Value)[] attributes) =>
        new(
            resource.Type,
            resource.Id,
            courseId,
            ownerId,
            status,
            attributes.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal));

    private static (string Key, AttributeValue Value) Text(string key, string value) =>
        (key, AttributeValue.Of(value));

    private static (string Key, AttributeValue Value) Missing(string key) =>
        (key, AttributeValue.Missing);

    private static CourseMembership Member(
        Guid accountId,
        Guid courseId,
        CourseRole role,
        MembershipStatus status = MembershipStatus.Active) =>
        new(accountId, courseId, role, status, FrozenSet<string>.Empty);
}