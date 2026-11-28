using System.Collections.ObjectModel;
using Platform.Access.Application.Services;
using Platform.Access.Contracts;
using Platform.Access.Domain.Models;
using Platform.Access.Domain.ValueObjects;

namespace Platform.Access.Application.Snapshots;

public sealed class EvaluationSnapshotLoader : IEvaluationSnapshotLoader
{
    private readonly IMembershipReader membershipReader;
    private readonly IElevatedGrantReader grantReader;
    private readonly IReadOnlyDictionary<string, IResourceAttributeResolver> resolvers;
    private readonly TimeProvider timeProvider;

    public EvaluationSnapshotLoader(
        IMembershipReader membershipReader,
        IEnumerable<IResourceAttributeResolver> resolvers,
        IElevatedGrantReader grantReader,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(membershipReader);
        ArgumentNullException.ThrowIfNull(resolvers);
        ArgumentNullException.ThrowIfNull(grantReader);
        ArgumentNullException.ThrowIfNull(timeProvider);

        var byType = new Dictionary<string, IResourceAttributeResolver>(StringComparer.Ordinal);
        foreach (var resolver in resolvers)
        {
            if (resolver is null || !ResourceType.IsValid(resolver.ResourceType))
            {
                throw new AccessConfigurationException("Resource attribute resolver has a missing or invalid ResourceType.");
            }

            if (!byType.TryAdd(resolver.ResourceType, resolver))
            {
                throw new AccessConfigurationException(
                    $"More than one resource attribute resolver is registered for '{resolver.ResourceType}'.");
            }
        }

        this.membershipReader = membershipReader;
        this.grantReader = grantReader;
        this.resolvers = byType;
        this.timeProvider = timeProvider;
    }

    public async ValueTask<EvaluationSnapshot> LoadAsync(AccessRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var subject = request.Subject
            ?? throw new ArgumentException("A snapshot requires an authenticated subject.", nameof(request));

        var now = timeProvider.GetUtcNow();
        var accountId = ParseAccountId(subject.Id);

        var resourceTask = ResolveResourceAsync(request.Resource, subject, cancellationToken);
        var grantsTask = LoadGrantsAsync(accountId, request.Action, cancellationToken);
        ObserveFault(resourceTask);
        ObserveFault(grantsTask);

        var both = Task.WhenAll(resourceTask, grantsTask);
        ObserveFault(both);
        await both.WaitAsync(cancellationToken).ConfigureAwait(false);

        var resource = resourceTask.Result;
        var grants = grantsTask.Result;
        var membership = await LoadMembershipAsync(accountId, resource, cancellationToken).ConfigureAwait(false);

        return new EvaluationSnapshot(subject, membership, resource, grants, now);
    }

    private async Task<ResourceDescriptor?> ResolveResourceAsync(ResourceRef? resource, SubjectRef subject, CancellationToken cancellationToken)
    {
        if (resource?.Type is null || !resolvers.TryGetValue(resource.Type, out var resolver))
        {
            return null;
        }

        var descriptor = await resolver.ResolveAsync(resource, subject, cancellationToken).ConfigureAwait(false);
        if (descriptor is null)
        {
            return null;
        }

        if (!string.Equals(descriptor.Type, resource.Type, StringComparison.Ordinal) ||
            !string.Equals(descriptor.Id, resource.Id, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Resolver returned a descriptor for a different resource.");
        }

        return Freeze(descriptor);
    }

    private async Task<IReadOnlyList<ElevatedGrant>> LoadGrantsAsync(
        Guid? accountId,
        string action,
        CancellationToken cancellationToken)
    {
        if (accountId is not { } holderId || string.IsNullOrEmpty(action))
        {
            return Array.Empty<ElevatedGrant>();
        }

        var loaded = await grantReader.GetForHolderAsync(holderId, action, cancellationToken).ConfigureAwait(false);
        if (loaded is null)
        {
            return Array.Empty<ElevatedGrant>();
        }

        return Array.AsReadOnly(
            loaded
                .Where(grant => grant is not null &&
                                grant.HolderId == holderId &&
                                string.Equals(grant.Action, action, StringComparison.Ordinal))
                .ToArray());
    }

    private async Task<CourseMembership?> LoadMembershipAsync(
        Guid? accountId,
        ResourceDescriptor? resource,
        CancellationToken cancellationToken)
    {
        if (accountId is not { } holderId || resource?.CourseId is not { } courseId || courseId == Guid.Empty)
        {
            return null;
        }

        var task = membershipReader.GetAsync(holderId, courseId, cancellationToken).AsTask();
        ObserveFault(task);
        var membership = await task.WaitAsync(cancellationToken).ConfigureAwait(false);

        if (membership is not null && (membership.AccountId != holderId || membership.CourseId != courseId))
        {
            throw new InvalidOperationException("Membership reader returned a membership for a different account or course.");
        }

        return membership;
    }

    private static ResourceDescriptor Freeze(ResourceDescriptor descriptor)
    {
        var copy = new Dictionary<string, AttributeValue>(StringComparer.Ordinal);
        if (descriptor.Attributes is not null)
        {
            foreach (var attribute in descriptor.Attributes)
            {
                copy[attribute.Key] = attribute.Value;
            }
        }

        return descriptor with { Attributes = new ReadOnlyDictionary<string, AttributeValue>(copy) };
    }

    private static Guid? ParseAccountId(string? subjectId) =>
        Guid.TryParseExact(subjectId, "D", out var id) && id != Guid.Empty ? id : null;

    private static void ObserveFault(Task task) =>
        task.ContinueWith(
            static completed => _ = completed.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
}
