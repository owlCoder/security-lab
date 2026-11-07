using Platform.Api.Modules.Laboratories.Domain;

namespace Platform.Api.Modules.Laboratories.Application;

public enum LabSearchStatus
{
    Ok,
    InvalidQuery,
    Unauthenticated
}

public sealed record LabSearchResult(LabSearchStatus Status, IReadOnlyList<Laboratory> Items, bool Truncated)
{
    public static LabSearchResult Ok(IReadOnlyList<Laboratory> items, bool truncated) =>
        new(LabSearchStatus.Ok, items, truncated);

    public static readonly LabSearchResult InvalidQuery = new(LabSearchStatus.InvalidQuery, [], false);
    public static readonly LabSearchResult Unauthenticated = new(LabSearchStatus.Unauthenticated, [], false);
}

public sealed class LaboratorySearchService(
    ILaboratoryRepository repository,
    IAccessDecision accessDecision,
    ILogger<LaboratorySearchService> logger)
{
    public const int MaxResults = 50;

    public async Task<LabSearchResult> SearchAsync(
        string? query, IIdentityContext caller, CancellationToken cancellationToken)
    {
        Guid? callerId = CallerId.From(caller);
        if (callerId is null)
        {
            return LabSearchResult.Unauthenticated;
        }

        if (!LabTitleSearch.TryCreateKey(query, out string? key))
        {
            return LabSearchResult.InvalidQuery;
        }

        IReadOnlyList<Laboratory> laboratories = await repository.ListAsync(cancellationToken);

        List<Laboratory> matches = laboratories
            .Where(lab => CanRead(caller, callerId.Value, lab))
            .Select(lab => (Lab: lab, Folded: LabTitleSearch.Fold(lab.Title)))
            .Where(x => x.Folded.Contains(key, StringComparison.Ordinal))
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

    private bool CanRead(IIdentityContext caller, Guid callerId, Laboratory laboratory) =>
        LaboratoryVisibility.IsVisibleTo(laboratory, callerId)
        && accessDecision.Decide(caller, LabActions.Read, laboratory.Id.ToString()).Allowed;
}
