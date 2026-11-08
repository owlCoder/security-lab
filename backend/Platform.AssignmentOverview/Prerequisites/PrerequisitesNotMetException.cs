namespace Platform.AssignmentOverview.Prerequisites;
 
// ASN-07 TIM 16
// Baca se kad student pokusa da pokrene vezbu ciji preduslovi nisu ispunjeni.
// API sloj ga pretvara u 403 sa kodom, bez ID-jeva u poruci.
public sealed class PrerequisitesNotMetException : Exception
{
    public PrerequisitesNotMetException(
        string reasonCode,
        IReadOnlyList<Guid> missingLaboratoryIds)
        : base("Prerequisites are not met.")
    {
        ReasonCode = reasonCode;
        MissingLaboratoryIds = missingLaboratoryIds;
    }
 
    public string ReasonCode { get; }
 
    public IReadOnlyList<Guid> MissingLaboratoryIds { get; }
}