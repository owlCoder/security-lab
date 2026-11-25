using Platform.Access.Contracts;
using Platform.Access.Domain.Constants;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Access.Infrastructure.Development;
using Platform.Access.Infrastructure.Guards;
using Platform.Access.Infrastructure.Membership;
using Platform.Access.Infrastructure.Resources;
using Platform.Access.Tests.Support;

namespace Platform.Access.Tests;

[TestFixture]
[Property("RequirementId", "ACC-08")]
public sealed class InMemoryResourceAttributeResolverTests
{
    private static readonly Guid Course = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    [Test]
    public async Task ACC08_KnownResource_ReturnsDescriptor()
    {
        var resolver = new InMemoryResourceAttributeResolver("attempt");
        resolver.Set(Descriptor("attempt", "a-1", status: "InProgress"));

        var descriptor = await resolver.ResolveAsync(new ResourceRef("attempt", "a-1"), CancellationToken.None);

        Assert.That(descriptor, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(descriptor!.Type, Is.EqualTo("attempt"));
            Assert.That(descriptor.Id, Is.EqualTo("a-1"));
            Assert.That(descriptor.CourseId, Is.EqualTo(Course));
            Assert.That(descriptor.Status, Is.EqualTo("InProgress"));
        });
    }

    [Test]
    public async Task ACC08_UnknownResource_ReturnsNull()
    {
        var resolver = new InMemoryResourceAttributeResolver("attempt");

        var descriptor = await resolver.ResolveAsync(new ResourceRef("attempt", "nema-ga"), CancellationToken.None);

        Assert.That(descriptor, Is.Null);
    }

    [Test]
    public async Task ACC08_ResourceOfAnotherType_ReturnsNull()
    {
        var resolver = new InMemoryResourceAttributeResolver("attempt");
        resolver.Set(Descriptor("attempt", "same-id"));

        var descriptor = await resolver.ResolveAsync(new ResourceRef("laboratory", "same-id"), CancellationToken.None);

        Assert.That(descriptor, Is.Null);
    }

    [Test]
    public void ACC08_DescriptorOfAnotherType_IsRejected()
    {
        var resolver = new InMemoryResourceAttributeResolver("attempt");

        Assert.Throws<ArgumentException>(() => resolver.Set(Descriptor("laboratory", "lab-1")));
    }

    [TestCase("")]
    [TestCase("   ")]
    public void ACC08_EmptyResourceType_IsRejected(string resourceType)
    {
        Assert.Throws<ArgumentException>(() => new InMemoryResourceAttributeResolver(resourceType));
    }

    [Test]
    public async Task ACC08_ChangingSourceAttributesAfterSet_DoesNotChangeResolvedDescriptor()
    {
        var source = new Dictionary<string, AttributeValue>
        {
            ["MaxAttempts"] = AttributeValue.Of(3)
        };
        var resolver = new InMemoryResourceAttributeResolver("assignment");
        resolver.Set(new ResourceDescriptor("assignment", "as-1", Course, null, "Active", source));

        source["MaxAttempts"] = AttributeValue.Of(99);
        source["Injected"] = AttributeValue.Of("x");
        var descriptor = await resolver.ResolveAsync(new ResourceRef("assignment", "as-1"), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(descriptor!.Attributes["MaxAttempts"].Value, Is.EqualTo(3));
            Assert.That(descriptor.Attributes.ContainsKey("Injected"), Is.False);
        });
    }

    [Test]
    public void ACC08_CancelledToken_StopsResolving()
    {
        var resolver = new InMemoryResourceAttributeResolver("attempt");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await resolver.ResolveAsync(new ResourceRef("attempt", "a-1"), cancellation.Token));
    }

    private static ResourceDescriptor Descriptor(string type, string id, string? status = null) =>
        new(type, id, Course, null, status, new Dictionary<string, AttributeValue>());
}

[TestFixture]
[Property("RequirementId", "ACC-08")]
public sealed class DevelopmentSeedGuardTests
{
    private IMembershipReader memberships = null!;
    private InMemoryResourceDescriptorResolver legacy = null!;
    private IReadOnlyList<InMemoryResourceAttributeResolver> resolvers = null!;

    [SetUp]
    public void SetUp()
    {
        var membershipReader = new InMemoryMembershipReader();
        legacy = new InMemoryResourceDescriptorResolver();
        AccessDevelopmentSeed.Populate(membershipReader, legacy);

        memberships = membershipReader;
        resolvers = AccessDevelopmentSeed.CreateAttributeResolvers();
    }

    [Test]
    public void ACC08_SeedCoversExactlyTheResourceTypesOfTheCourseActions()
    {
        var seeded = resolvers.Select(resolver => resolver.ResourceType).ToArray();
        var expected = CourseRolePolicies.All
            .Select(definition => definition.Action.ResourceType!)
            .Distinct()
            .ToArray();

        Assert.That(seeded, Is.EquivalentTo(expected));
    }

    [Test]
    public async Task ACC08_LegacyPortsAndAttributeSources_AgreeOnCourseAndOwner()
    {
        foreach (var resource in AccessDevelopmentSeed.AllResources)
        {
            var descriptor = await Resolve(resource);

            Assert.That(descriptor, Is.Not.Null, $"{resource.Type}:{resource.Id} nema deskriptor");
            Assert.That(
                await legacy.ResolveCourseIdAsync(resource),
                Is.EqualTo(descriptor!.CourseId),
                $"kurs se razilazi za {resource.Type}:{resource.Id}");
            Assert.That(
                await legacy.ResolveOwnerIdAsync(resource),
                Is.EqualTo(descriptor.OwnerId),
                $"vlasnik se razilazi za {resource.Type}:{resource.Id}");
        }
    }

    [Test]
    public async Task ACC08_IncompleteAssignment_ExposesMissingAttributesNotDefaults()
    {
        var descriptor = await Resolve(AccessDevelopmentSeed.AssignmentIncompleteA);

        Assert.That(descriptor, Is.Not.Null);
        foreach (var key in new[]
                 {
                     ResourceAttributeKeys.Availability,
                     ResourceAttributeKeys.DueAtUtc,
                     ResourceAttributeKeys.MaxAttempts,
                     ResourceAttributeKeys.AttemptsUsed
                 })
        {
            Assert.That(descriptor!.Attributes[key].IsPresent, Is.False, key);
            Assert.That(descriptor.Attributes[key].Value, Is.Null, key);
        }
    }

    [Test]
    public async Task ACC08_CompleteAssignment_ExposesTypedAttributes()
    {
        var descriptor = await Resolve(AccessDevelopmentSeed.AssignmentA);

        Assert.That(descriptor, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(descriptor!.Attributes[ResourceAttributeKeys.MaxAttempts].Value, Is.EqualTo(3));
            Assert.That(descriptor.Attributes[ResourceAttributeKeys.AttemptsUsed].Value, Is.EqualTo(1));
            Assert.That(descriptor.Attributes[ResourceAttributeKeys.DueAtUtc].Value, Is.InstanceOf<DateTimeOffset>());
            Assert.That(
                descriptor.Attributes[ResourceAttributeKeys.Availability].Value,
                Is.EqualTo(ResourceAttributeKeys.AssignmentAvailabilities.Available));
        });
    }

    [Test]
    public async Task ACC08_ExpiredAssignment_HasPastDueDate()
    {
        var descriptor = await Resolve(AccessDevelopmentSeed.AssignmentExpiredA);

        Assert.That(descriptor, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(
                descriptor!.Attributes[ResourceAttributeKeys.Availability].Value,
                Is.EqualTo(ResourceAttributeKeys.AssignmentAvailabilities.Expired));
            Assert.That(
                (DateTimeOffset)descriptor.Attributes[ResourceAttributeKeys.DueAtUtc].Value!,
                Is.LessThan(new DateTimeOffset(2021, 1, 1, 0, 0, 0, TimeSpan.Zero)));
        });
    }

    [Test]
    public async Task ACC08_Guard_StudentReadsOwnAttempt_Permit() =>
        Assert.That(
            await Guard(AccessDevelopmentSeed.StudentA, "attempt.read", AccessDevelopmentSeed.AttemptA),
            Is.EqualTo(PolicyOutcome.Permit));

    [Test]
    public async Task ACC08_Guard_StudentReadsAttemptOfAnotherStudent_Deny() =>
        Assert.That(
            await Guard(AccessDevelopmentSeed.StudentA, "attempt.read", AccessDevelopmentSeed.AttemptB),
            Is.EqualTo(PolicyOutcome.Deny));

    [Test]
    public async Task ACC08_Guard_StudentReadsAttemptOfClassmateInSameCourse_Deny() =>
        Assert.That(
            await Guard(
                AccessDevelopmentSeed.StudentA,
                "attempt.read",
                AccessDevelopmentSeed.AttemptOfFormerStudentA),
            Is.EqualTo(PolicyOutcome.Deny));

    [Test]
    public async Task ACC08_Guard_StudentOfAnotherCourseReadsAttempt_Deny() =>
        Assert.That(
            await Guard(AccessDevelopmentSeed.StudentB, "attempt.read", AccessDevelopmentSeed.AttemptA),
            Is.EqualTo(PolicyOutcome.Deny));

    private async Task<ResourceDescriptor?> Resolve(ResourceRef resource)
    {
        var resolver = resolvers.SingleOrDefault(
            candidate => string.Equals(candidate.ResourceType, resource.Type, StringComparison.Ordinal));

        return resolver is null
            ? null
            : await resolver.ResolveAsync(resource, CancellationToken.None);
    }

    private async Task<PolicyOutcome> Guard(Guid accountId, string action, ResourceRef resource)
    {
        var descriptor = await Resolve(resource);
        CourseMembership? membership = null;

        if (descriptor?.CourseId is Guid courseId)
        {
            membership = await memberships.GetAsync(accountId, courseId, CancellationToken.None);
        }

        var snapshot = EvaluationSnapshotBuilder.Default()
            .WithSubject(new SubjectRef(accountId.ToString("D"), AccountState.Active))
            .WithMembership(membership)
            .WithResource(descriptor)
            .Build();

        return new OwnershipScopeGuard().Evaluate(snapshot, action).Outcome;
    }
}