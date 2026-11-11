using Platform.Api.Modules.Laboratories.Domain;
using Platform.LabMetadata;
using Platform.LaboratoryPersistence.Application;
using Platform.LabVisibility.Contracts;

namespace Platform.Api.Modules.Laboratories.Application;

public enum LabSearchStatus
{
    Ok,
    InvalidQuery,
    Unauthenticated,
    FiltersUnavailable,
    Forbidden
}

public sealed record LabSearchResult(LabSearchStatus Status, IReadOnlyList<Laboratory> Items, bool Truncated)
{
    public static LabSearchResult Ok(IReadOnlyList<Laboratory> items, bool truncated) =>
        new(LabSearchStatus.Ok, items, truncated);

    public static readonly LabSearchResult InvalidQuery = new(LabSearchStatus.InvalidQuery, [], false);
    public static readonly LabSearchResult Unauthenticated = new(LabSearchStatus.Unauthenticated, [], false);
    public static readonly LabSearchResult FiltersUnavailable = new(LabSearchStatus.FiltersUnavailable, [], false);
}

public sealed class LaboratorySearchService(
    ILaboratoryRepository repository,
    ICourseMembership courseMembership,
    IAccessDecision accessDecision,
    ILogger<LaboratorySearchService> logger,
    ILaboratoryMetadataStore? metadataStore = null)
{
    public const int MaxResults = 50;

    public async Task<LabSearchResult> SearchAsync(
        string? query, IIdentityContext caller, CancellationToken cancellationToken,
        LabSearchFilters? filters = null)
    {
        Guid? callerId = CallerId.From(caller);
        if (callerId is null)
        {
            return LabSearchResult.Unauthenticated;
        }

        LabSearchStatus accessStatus = CheckSearchAccess(caller);
        if (accessStatus != LabSearchStatus.Ok)
        {
            return new(accessStatus, [], false);
        }

        if ((query?.Any(char.IsControl) ?? false)
            || !(filters ?? new LabSearchFilters()).TryNormalize(out LabSearchFilters normalizedFilters))
        {
            return LabSearchResult.InvalidQuery;
        }

        string key = string.Empty;
        if (!string.IsNullOrWhiteSpace(query))
        {
            if (!LabTitleSearch.TryCreateKey(query, out string? titleKey))
            {
                return LabSearchResult.InvalidQuery;
            }

            key = titleKey;
        }

        if (normalizedFilters.HasValues && metadataStore is null)
        {
            return LabSearchResult.FiltersUnavailable;
        }

        IReadOnlyList<Laboratory> laboratories = await repository.ListAsync(cancellationToken);

        var candidates = new List<(Laboratory Lab, string Folded)>();
        foreach (Laboratory laboratory in laboratories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!await CanReadAsync(caller, callerId.Value, laboratory, cancellationToken))
            {
                continue;
            }

            string folded = LabTitleSearch.Fold(laboratory.Title);
            if (!folded.Contains(key, StringComparison.Ordinal))
            {
                continue;
            }

            if (normalizedFilters.HasValues)
            {
                LaboratoryMetadata? metadata = await metadataStore!.GetAsync(laboratory.Id, cancellationToken);
                if (metadata is null || !normalizedFilters.Matches(metadata))
                {
                    continue;
                }
            }

            candidates.Add((laboratory, folded));
        }

        List<Laboratory> matches = candidates
            .OrderBy(x => x.Folded.StartsWith(key, StringComparison.Ordinal) ? 0 : 1)
            .ThenBy(x => x.Folded, StringComparer.Ordinal)
            .ThenBy(x => x.Lab.CreatedAt)
            .ThenBy(x => x.Lab.Id)
            .Take(MaxResults + 1)
            .Select(x => x.Lab)
            .ToList();

        bool truncated = matches.Count > MaxResults;
        if (truncated)
        {
            matches.RemoveAt(MaxResults);
        }

        logger.LogInformation(
            "Laboratory search: by={CallerId} queryLength={QueryLength} results={ResultCount} truncated={Truncated}",
            callerId.Value, key.Length, matches.Count, truncated);

        return LabSearchResult.Ok(matches, truncated);
    }

    public async Task<LabSearchFilterOptions> GetFilterOptionsAsync(
        IIdentityContext caller, CancellationToken cancellationToken)
    {
        Guid? callerId = CallerId.From(caller);
        if (callerId is null)
        {
            return new(LabSearchStatus.Unauthenticated, [], [], [], false);
        }

        LabSearchStatus accessStatus = CheckSearchAccess(caller);
        if (accessStatus != LabSearchStatus.Ok)
        {
            return new(accessStatus, [], [], [], false);
        }

        string[] difficulties = Enum.GetNames<LaboratoryDifficulty>();
        string[] types = Enum.GetNames<LaboratoryType>();
        if (metadataStore is null)
        {
            return new(LabSearchStatus.Ok, difficulties, types, [], false);
        }

        var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Laboratory laboratory in await repository.ListAsync(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!await CanReadAsync(caller, callerId.Value, laboratory, cancellationToken))
            {
                continue;
            }

            LaboratoryMetadata? metadata = await metadataStore.GetAsync(laboratory.Id, cancellationToken);
            if (metadata is not null)
            {
                tags.UnionWith(metadata.Tags.Where(tag => !string.IsNullOrWhiteSpace(tag) && !tag.Any(char.IsControl)));
            }
        }

        return new(LabSearchStatus.Ok, difficulties, types,
            tags.Order(StringComparer.OrdinalIgnoreCase).ToArray(), true);
    }

    private LabSearchStatus CheckSearchAccess(IIdentityContext caller)
    {
        AccessResult decision = accessDecision.Decide(caller, LabActions.Search);
        return decision.Allowed ? LabSearchStatus.Ok
            : decision.Reason == DenyReason.Unauthenticated ? LabSearchStatus.Unauthenticated
            : LabSearchStatus.Forbidden;
    }

    private async ValueTask<bool> CanReadAsync(
        IIdentityContext caller, Guid callerId, Laboratory laboratory, CancellationToken cancellationToken)
    {
        if (!accessDecision.Decide(caller, LabActions.Read, laboratory.Id.ToString()).Allowed
            || laboratory.CourseId == Guid.Empty)
        {
            return false;
        }

        CourseRole role = await courseMembership.GetRoleAsync(laboratory.CourseId, callerId, cancellationToken);
        return LabOpenPolicy.DecideOpen(laboratory.CourseId, role,
            isCreator: laboratory.CreatedBy == callerId).Allowed;
    }
}
