using Platform.Authoring.Application;
using Platform.Authoring.Domain;

namespace Platform.Authoring.Infrastructure;

/// <summary>
/// Starter templates for the three core laboratory types (TYP-01..TYP-03, scenarios 5.1..5.3).
/// Ids are fixed so that they stay stable across restarts and migrations.
/// </summary>
public static class BuiltInTemplates
{
    public static readonly Guid AccessControlId = Guid.Parse("a1000000-0000-4000-8000-000000000001");
    public static readonly Guid LogAnalysisId = Guid.Parse("a1000000-0000-4000-8000-000000000002");
    public static readonly Guid SecureConfigurationId = Guid.Parse("a1000000-0000-4000-8000-000000000003");

    public static IReadOnlyList<LabTemplate> All { get; } =
    [
        LabTemplate.Create(
            AccessControlId,
            "Access control and ownership",
            LabContent.Create(
                "Access control and ownership",
                "Given a user, a resource, a requested action and a set of permissions, decide whether access " +
                "should be allowed and justify the decision. Watch for reliance on login or role alone, access to " +
                "another user's resource, swapped IDs and non-existent membership.",
                LabType.AccessControl)),
        LabTemplate.Create(
            LogAnalysisId,
            "Security log analysis",
            LabContent.Create(
                "Security log analysis",
                "Given a fixed set of timestamped security events, select the related records and explain why " +
                "they matter. Watch for false alarms, duplicates and misread time zones.",
                LabType.LogAnalysis)),
        LabTemplate.Create(
            SecureConfigurationId,
            "Secure configuration review",
            LabContent.Create(
                "Secure configuration review",
                "Given the configuration of a simulated service and the rules for permitted values, find the " +
                "insecure settings and propose changes. Watch for allowed exceptions, missing settings and " +
                "contradictory policies.",
                LabType.SecureConfiguration)),
    ];
}

/// <summary>Read-only repository over <see cref="BuiltInTemplates"/>. Immutable data, so it is safe to share.</summary>
public sealed class BuiltInTemplateRepository : ITemplateRepository
{
    public Task<LabTemplate?> GetAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(BuiltInTemplates.All.FirstOrDefault(t => t.Id == id));

    public Task<IReadOnlyList<LabTemplate>> ListAsync(CancellationToken ct = default) =>
        Task.FromResult(BuiltInTemplates.All);
}
