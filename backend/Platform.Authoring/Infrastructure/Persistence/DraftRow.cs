namespace Platform.Authoring.Infrastructure.Persistence;

/// <summary>
/// Storage shape of a draft (table authoring.drafts). It only holds what the domain model holds:
/// ids, status, version and the three content fields. No credentials, tokens or student data.
/// </summary>
internal sealed class DraftRow
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public Guid? SourceTemplateId { get; set; }
    public int Status { get; set; }
    public string Title { get; set; } = "";
    public string Goal { get; set; } = "";
    public int LabType { get; set; }
    public int Version { get; set; }
}
