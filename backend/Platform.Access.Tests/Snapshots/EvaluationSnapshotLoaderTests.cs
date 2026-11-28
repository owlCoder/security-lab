using Platform.Access.Application.Services;
using Platform.Access.Application.Snapshots;
using Platform.Access.Contracts;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Access.Tests.Support;

namespace Platform.Access.Tests.Snapshots;

[TestFixture]
[Property("RequirementId", "ACC-08")]
public class EvaluationSnapshotLoaderTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Account = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Course = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static AccessRequest Request(
        string? subjectId = null,
        string action = "attempt.read",
        ResourceRef? resource = null) =>
        new(new SubjectRef(subjectId ?? Account.ToString("D"), AccountState.Active),
            action,
            resource ?? new ResourceRef("attempt", "att-1"),
            "corr-1");

    private static ResourceDescriptor Descriptor(
        string type = "attempt",
        string id = "att-1",
        Guid? course = null,
        IReadOnlyDictionary<string, AttributeValue>? attributes = null) =>
        new(type, id, course ?? Course, Guid.NewGuid(), "InProgress",
            attributes ?? new Dictionary<string, AttributeValue>());

    private static CourseMembership Membership(Guid? account = null, Guid? course = null) =>
        new(account ?? Account, course ?? Course, CourseRole.Student, MembershipStatus.Active, new HashSet<string>());

    private static ElevatedGrant Grant(Guid holder, string action = "attempt.read") =>
        new(Guid.NewGuid(), holder, Guid.NewGuid(), action,
            new GrantScope(GrantScopeKind.Course, Course.ToString("D")),
            "reason", Start.AddHours(-1), Start.AddHours(1), false);

    private static EvaluationSnapshotLoader Loader(
        FakeMembershipReader? membership = null,
        FakeGrantReader? grants = null,
        TimeProvider? time = null,
        IResourceAttributeResolver? resolver = null,
        IReadOnlyList<IResourceAttributeResolver>? resolvers = null) =>
        new(membership ?? new FakeMembershipReader(),
            resolvers ?? (resolver is null ? [] : [resolver]),
            grants ?? new FakeGrantReader(),
            time ?? new ManualTimeProvider(Start));

    private static FakeResourceResolver ResolverReturning(ResourceDescriptor? descriptor, string type = "attempt") =>
        new(type, (_, _) => ValueTask.FromResult(descriptor));

    // ---------- Pozitivni tok ----------

    [Test]
    public async Task ACC08_Load_AssemblesSubjectResourceMembershipGrantsAndNow()
    {
        var grant = Grant(Account);
        var loader = Loader(
            new FakeMembershipReader((_, _, _) => ValueTask.FromResult<CourseMembership?>(Membership())),
            new FakeGrantReader((_, _, _) => ValueTask.FromResult<IReadOnlyList<ElevatedGrant>>([grant])),
            new ManualTimeProvider(Start),
            ResolverReturning(Descriptor()));

        var snapshot = await loader.LoadAsync(Request(), CancellationToken.None);

        Assert.That(snapshot.Subject.Id, Is.EqualTo(Account.ToString("D")));
        Assert.That(snapshot.Resource!.Id, Is.EqualTo("att-1"));
        Assert.That(snapshot.Membership!.CourseId, Is.EqualTo(Course));
        Assert.That(snapshot.Grants.Count, Is.EqualTo(1));
        Assert.That(snapshot.Grants[0], Is.EqualTo(grant));
        Assert.That(snapshot.Now, Is.EqualTo(Start));
    }

    [Test]
    public async Task ACC08_Load_PassesSubjectToResourceResolver()
    {
        SubjectRef? receivedSubject = null;
        var resolver = new FakeResourceResolver(
            "attempt",
            subjectHandler: (_, subject, _) =>
            {
                receivedSubject = subject;
                return ValueTask.FromResult<ResourceDescriptor?>(Descriptor());
            });
        var requestSubject = new SubjectRef(Account.ToString("D"), AccountState.Active);
        var request = new AccessRequest(
            requestSubject,
            "attempt.read",
            new ResourceRef("attempt", "att-1"),
            "corr-subject");

        var snapshot = await Loader(resolver: resolver).LoadAsync(request, CancellationToken.None);

        Assert.That(snapshot.Subject, Is.SameAs(requestSubject));
        Assert.That(receivedSubject, Is.SameAs(requestSubject));
        Assert.That(resolver.Subjects, Has.Count.EqualTo(1));
        Assert.That(resolver.Subjects[0], Is.SameAs(requestSubject));
    }

    [Test]
    public async Task ACC08_Load_MembershipIsReadForTheCourseOfTheResolvedResource()
    {
        var membership = new FakeMembershipReader();
        var loader = Loader(membership, resolver: ResolverReturning(Descriptor()));

        await loader.LoadAsync(Request(), CancellationToken.None);

        Assert.That(membership.Calls.Count, Is.EqualTo(1));
        Assert.That(membership.Calls[0], Is.EqualTo((Account, Course)));
    }

    [Test]
    public async Task ACC08_Load_GrantsAreReadForSubjectAndRequestedAction()
    {
        var grants = new FakeGrantReader();
        var loader = Loader(grants: grants, resolver: ResolverReturning(Descriptor()));

        await loader.LoadAsync(Request(action: "results.sensitive.read"), CancellationToken.None);

        Assert.That(grants.Calls.Count, Is.EqualTo(1));
        Assert.That(grants.Calls[0], Is.EqualTo((Account, "results.sensitive.read")));
    }

    [Test]
    public async Task ACC08_Load_TimeIsTakenOnceFromTimeProviderAtEachDecision()
    {
        var clock = new ManualTimeProvider(Start);
        var loader = Loader(time: clock, resolver: ResolverReturning(Descriptor()));

        var first = await loader.LoadAsync(Request(), CancellationToken.None);
        clock.Advance(TimeSpan.FromMinutes(5));
        var second = await loader.LoadAsync(Request(), CancellationToken.None);

        Assert.That(first.Now, Is.EqualTo(Start));
        Assert.That(second.Now, Is.EqualTo(Start.AddMinutes(5)));
    }

    // ---------- Nedostajući podaci su Missing, ne greška ----------

    [Test]
    public async Task ACC08_Load_NoResolverForType_ResourceIsMissing()
    {
        var loader = Loader(resolver: ResolverReturning(Descriptor(), type: "assignment"));

        var snapshot = await loader.LoadAsync(Request(), CancellationToken.None);

        Assert.That(snapshot.Resource, Is.Null);
        Assert.That(snapshot.Membership, Is.Null);
    }

    [Test]
    public async Task ACC08_Load_ResolverReturnsNull_ResourceIsMissingAndMembershipNotRead()
    {
        var membership = new FakeMembershipReader();
        var loader = Loader(membership, resolver: ResolverReturning(null));

        var snapshot = await loader.LoadAsync(Request(), CancellationToken.None);

        Assert.That(snapshot.Resource, Is.Null);
        Assert.That(membership.Calls.Count, Is.EqualTo(0));
    }

    [Test]
    public async Task ACC08_Load_RequestWithoutResource_ResourceAndMembershipAreMissing()
    {
        var resolver = ResolverReturning(Descriptor());
        var request = new AccessRequest(new SubjectRef(Account.ToString("D"), AccountState.Active), "attempt.read", null, "c");

        var snapshot = await Loader(resolver: resolver).LoadAsync(request, CancellationToken.None);

        Assert.That(snapshot.Resource, Is.Null);
        Assert.That(resolver.Calls, Is.EqualTo(0));
    }

    [Test]
    public async Task ACC08_Load_ResourceWithoutCourse_MembershipNotRead()
    {
        var membership = new FakeMembershipReader();
        var loader = Loader(membership, resolver: ResolverReturning(Descriptor() with { CourseId = null }));

        var snapshot = await loader.LoadAsync(Request(), CancellationToken.None);

        Assert.That(snapshot.Membership, Is.Null);
        Assert.That(membership.Calls.Count, Is.EqualTo(0));
    }

    [TestCase("not-a-guid")]
    [TestCase("")]
    [TestCase("00000000-0000-0000-0000-000000000000")]
    [TestCase("11111111111111111111111111111111")] // format "N" se ne prihvata (strogo "D")
    public async Task ACC08_Load_SubjectIdThatIsNotAGuid_NoMembershipNoGrantsAndReadersNotCalled(string subjectId)
    {
        var membership = new FakeMembershipReader();
        var grants = new FakeGrantReader();
        var loader = Loader(membership, grants, resolver: ResolverReturning(Descriptor()));

        var snapshot = await loader.LoadAsync(Request(subjectId: subjectId), CancellationToken.None);

        Assert.That(snapshot.Membership, Is.Null);
        Assert.That(snapshot.Grants.Count, Is.EqualTo(0));
        Assert.That(membership.Calls.Count, Is.EqualTo(0));
        Assert.That(grants.Calls.Count, Is.EqualTo(0));
    }

    // ---------- Greške se propagiraju (engine ih pretvara u Deny/PolicyError) ----------

    [Test]
    public void ACC08_Load_ResolverThrows_ExceptionPropagates()
    {
        var resolver = new FakeResourceResolver("attempt", (_, _) => throw new InvalidOperationException("resolver down"));

        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await Loader(resolver: resolver).LoadAsync(Request(), CancellationToken.None));
    }

    [Test]
    public void ACC08_Load_ResolverFaultsAsynchronously_ExceptionPropagates()
    {
        var resolver = new FakeResourceResolver("attempt", async (_, _) =>
        {
            await Task.Yield();
            throw new InvalidOperationException("resolver down");
        });

        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await Loader(resolver: resolver).LoadAsync(Request(), CancellationToken.None));
    }

    [Test]
    public void ACC08_Load_GrantReaderThrows_ExceptionPropagates()
    {
        var grants = new FakeGrantReader((_, _, _) => throw new InvalidOperationException("grants down"));

        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await Loader(grants: grants, resolver: ResolverReturning(Descriptor())).LoadAsync(Request(), CancellationToken.None));
    }

    [Test]
    public void ACC08_Load_MembershipReaderThrows_ExceptionPropagates()
    {
        var membership = new FakeMembershipReader((_, _, _) => throw new InvalidOperationException("members down"));

        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await Loader(membership, resolver: ResolverReturning(Descriptor())).LoadAsync(Request(), CancellationToken.None));
    }

    // ---------- Otkazivanje i spori izvori ----------

    [Test]
    public void ACC08_Load_SlowResolverIgnoringToken_ReturnsOnCancellation()
    {
        var resolver = new FakeResourceResolver("attempt", (_, _) =>
            new ValueTask<ResourceDescriptor?>(new TaskCompletionSource<ResourceDescriptor?>().Task));
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
        var started = DateTime.UtcNow;

        // Gornja granica čekanja: regresija postaje pad testa (TimeoutException), a ne zastoj cele suite.
        Assert.CatchAsync<OperationCanceledException>(async () =>
            await Loader(resolver: resolver).LoadAsync(Request(), cts.Token).AsTask().WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.That(DateTime.UtcNow - started, Is.LessThan(TimeSpan.FromSeconds(5)));
    }

    [Test]
    public void ACC08_Load_SlowMembershipReaderIgnoringToken_ReturnsOnCancellation()
    {
        var membership = new FakeMembershipReader((_, _, _) =>
            new ValueTask<CourseMembership?>(new TaskCompletionSource<CourseMembership?>().Task));
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));

        Assert.CatchAsync<OperationCanceledException>(async () =>
            await Loader(membership, resolver: ResolverReturning(Descriptor())).LoadAsync(Request(), cts.Token)
                .AsTask().WaitAsync(TimeSpan.FromSeconds(10)));
    }

    [Test]
    public void ACC08_Load_AlreadyCancelledToken_ThrowsWithoutCallingAnySource()
    {
        var resolver = ResolverReturning(Descriptor());
        var membership = new FakeMembershipReader();
        var grants = new FakeGrantReader();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.CatchAsync<OperationCanceledException>(async () =>
            await Loader(membership, grants, resolver: resolver).LoadAsync(Request(), cts.Token));
        Assert.That(resolver.Calls, Is.EqualTo(0));
        Assert.That(membership.Calls.Count + grants.Calls.Count, Is.EqualTo(0));
    }

    [Test]
    public async Task ACC08_Load_ResourceAndGrantsAreLoadedConcurrently()
    {
        var release = new TaskCompletionSource<ResourceDescriptor?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var grantsStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resolver = new FakeResourceResolver("attempt", (_, _) => new ValueTask<ResourceDescriptor?>(release.Task));
        var grants = new FakeGrantReader((_, _, _) =>
        {
            grantsStarted.TrySetResult();
            return ValueTask.FromResult<IReadOnlyList<ElevatedGrant>>(Array.Empty<ElevatedGrant>());
        });

        var load = Loader(grants: grants, resolver: resolver).LoadAsync(Request(), CancellationToken.None).AsTask();

        // Resolver je još blokiran, a čitač granta je već pozvan: nije sekvencijalno.
        await grantsStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(load.IsCompleted, Is.False);

        release.SetResult(Descriptor());
        var snapshot = await load.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.That(snapshot.Resource, Is.Not.Null);
    }

    [Test]
    public async Task ACC08_Load_SourceFailingAfterCancellation_LeavesNoUnobservedFault()
    {
        var resolver = new FakeResourceResolver("attempt", async (_, _) =>
        {
            await Task.Delay(150);
            throw new InvalidOperationException("resolver down after the caller stopped waiting");
        });
        var unobserved = 0;
        void Handler(object? sender, UnobservedTaskExceptionEventArgs args) => Interlocked.Increment(ref unobserved);
        TaskScheduler.UnobservedTaskException += Handler;

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(30));
            Assert.CatchAsync<OperationCanceledException>(async () =>
                await Loader(resolver: resolver).LoadAsync(Request(), cts.Token));

            await Task.Delay(500); // izvor pada tek posle otkazivanja
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= Handler;
        }

        Assert.That(unobserved, Is.EqualTo(0));
    }

    // ---------- Odbrana u dubini ----------

    [TestCase("laboratory", "att-1")]
    [TestCase("attempt", "att-2")]
    public void ACC08_Load_DescriptorForDifferentResource_Rejected(string type, string id)
    {
        var loader = Loader(resolver: ResolverReturning(Descriptor(type: type, id: id)));

        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await loader.LoadAsync(Request(), CancellationToken.None));
    }

    [Test]
    public void ACC08_Load_MembershipOfAnotherAccountOrCourse_Rejected()
    {
        var otherAccount = new FakeMembershipReader((_, _, _) =>
            ValueTask.FromResult<CourseMembership?>(Membership(account: Guid.NewGuid())));
        var otherCourse = new FakeMembershipReader((_, _, _) =>
            ValueTask.FromResult<CourseMembership?>(Membership(course: Guid.NewGuid())));

        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await Loader(otherAccount, resolver: ResolverReturning(Descriptor())).LoadAsync(Request(), CancellationToken.None));
        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await Loader(otherCourse, resolver: ResolverReturning(Descriptor())).LoadAsync(Request(), CancellationToken.None));
    }

    [Test]
    public async Task ACC08_Load_GrantsOfOtherHoldersOrActions_AreDropped()
    {
        var mine = Grant(Account);
        var foreignHolder = Grant(Guid.NewGuid());
        var otherAction = Grant(Account, "lab.read");
        var wrongCase = Grant(Account, "Attempt.Read");
        var grants = new FakeGrantReader((_, _, _) =>
            ValueTask.FromResult<IReadOnlyList<ElevatedGrant>>([mine, foreignHolder, otherAction, wrongCase, null!]));

        var snapshot = await Loader(grants: grants, resolver: ResolverReturning(Descriptor()))
            .LoadAsync(Request(), CancellationToken.None);

        Assert.That(snapshot.Grants.Count, Is.EqualTo(1));
        Assert.That(snapshot.Grants[0], Is.EqualTo(mine));
    }

    [Test]
    public async Task ACC08_Load_GrantReaderReturningNull_IsEmptyNotError()
    {
        var grants = new FakeGrantReader((_, _, _) => ValueTask.FromResult<IReadOnlyList<ElevatedGrant>>(null!));

        var snapshot = await Loader(grants: grants, resolver: ResolverReturning(Descriptor()))
            .LoadAsync(Request(), CancellationToken.None);

        Assert.That(snapshot.Grants.Count, Is.EqualTo(0));
    }

    [Test]
    public async Task ACC08_Load_AttributesAreCopied_LaterSourceChangeDoesNotAffectSnapshot()
    {
        var source = new Dictionary<string, AttributeValue> { ["Deadline"] = AttributeValue.Of("v1") };
        var loader = Loader(resolver: ResolverReturning(Descriptor(attributes: source)));

        var snapshot = await loader.LoadAsync(Request(), CancellationToken.None);
        source["Deadline"] = AttributeValue.Of("tampered");
        source["Extra"] = AttributeValue.Of(1);

        Assert.That(snapshot.Resource!.Attributes["Deadline"].Value, Is.EqualTo("v1"));
        Assert.That(snapshot.Resource.Attributes.ContainsKey("Extra"), Is.False);
    }

    [Test]
    public async Task ACC08_Load_AttributeKeysAreOrdinal_CaseVariantIsNotFound()
    {
        var source = new Dictionary<string, AttributeValue>(StringComparer.OrdinalIgnoreCase)
        {
            ["Deadline"] = AttributeValue.Of("v1")
        };

        var snapshot = await Loader(resolver: ResolverReturning(Descriptor(attributes: source)))
            .LoadAsync(Request(), CancellationToken.None);

        Assert.That(snapshot.Resource!.Attributes.ContainsKey("Deadline"), Is.True);
        Assert.That(snapshot.Resource.Attributes.ContainsKey("deadline"), Is.False);
    }

    [Test]
    public async Task ACC08_Load_SnapshotGrantListCannotBeChangedThroughTheSourceList()
    {
        var list = new List<ElevatedGrant> { Grant(Account) };
        var grants = new FakeGrantReader((_, _, _) => ValueTask.FromResult<IReadOnlyList<ElevatedGrant>>(list));

        var snapshot = await Loader(grants: grants, resolver: ResolverReturning(Descriptor()))
            .LoadAsync(Request(), CancellationToken.None);
        list.Clear();

        Assert.That(snapshot.Grants.Count, Is.EqualTo(1));
    }

    // ---------- Neispravan ulaz i konfiguracija ----------

    [Test]
    public void ACC08_Load_NullRequest_Rejected() =>
        Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await Loader().LoadAsync(null!, CancellationToken.None));

    [Test]
    public void ACC08_Load_RequestWithoutSubject_Rejected()
    {
        var request = new AccessRequest(null, "attempt.read", new ResourceRef("attempt", "att-1"), "c");

        Assert.ThrowsAsync<ArgumentException>(async () =>
            await Loader(resolver: ResolverReturning(Descriptor())).LoadAsync(request, CancellationToken.None));
    }

    [Test]
    public void ACC08_Construction_TwoResolversForSameType_Rejected() =>
        Assert.That(
            () => Loader(resolvers: [ResolverReturning(null), ResolverReturning(null)]),
            Throws.InstanceOf<AccessConfigurationException>());

    [TestCase("")]
    [TestCase("Attempt")]
    [TestCase("att empty")]
    public void ACC08_Construction_InvalidResolverResourceType_Rejected(string type) =>
        Assert.That(
            () => Loader(resolver: ResolverReturning(null, type)),
            Throws.InstanceOf<AccessConfigurationException>());

    [Test]
    public void ACC08_Construction_NullDependencies_Rejected()
    {
        var clock = new ManualTimeProvider(Start);

        Assert.That(() => new EvaluationSnapshotLoader(null!, [], new FakeGrantReader(), clock), Throws.InstanceOf<ArgumentNullException>());
        Assert.That(() => new EvaluationSnapshotLoader(new FakeMembershipReader(), null!, new FakeGrantReader(), clock), Throws.InstanceOf<ArgumentNullException>());
        Assert.That(() => new EvaluationSnapshotLoader(new FakeMembershipReader(), [], null!, clock), Throws.InstanceOf<ArgumentNullException>());
        Assert.That(() => new EvaluationSnapshotLoader(new FakeMembershipReader(), [], new FakeGrantReader(), null!), Throws.InstanceOf<ArgumentNullException>());
    }
}
