namespace Platform.AssignmentOverview.Prerequisites;
 
// ASN-07 TIM 16
// Privremena implementacija za KT1, za testove i Development. Nepoznata revizija vraca null.
public sealed class InMemoryRevisionLaboratoryResolver : IRevisionLaboratoryResolver
{
    private readonly Dictionary<string, Guid> _map = new();
 
    public void Map(string publishedLabRevisionId, Guid laboratoryId)
    {
        _map[publishedLabRevisionId] = laboratoryId;
    }
 
    public ValueTask<Guid?> ResolveLaboratoryIdAsync(
        string publishedLabRevisionId,
        CancellationToken cancellationToken = default)
    {
        Guid? result = !string.IsNullOrWhiteSpace(publishedLabRevisionId)
                       && _map.TryGetValue(publishedLabRevisionId, out Guid laboratoryId)
            ? laboratoryId
            : null;
 
        return ValueTask.FromResult(result);
    }
}