namespace Team21.Acc.Core.Groups.Visibility.Interfaces;

/// <summary>Writes visibility denials to the audit log. Owned by AUD.</summary>
public interface IVisibilityAuditWriter
{
    Task WriteDenialAsync(VisibilityDenial denial, CancellationToken ct = default);
}
