namespace Team21.Acc.Core.Interfaces;

/// <summary>Id of the current request, used to link audit records to it.</summary>
public interface ICorrelationContext
{
    string CorrelationId { get; }
}
