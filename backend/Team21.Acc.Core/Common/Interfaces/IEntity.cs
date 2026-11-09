namespace Team21.Acc.Core.Common.Interfaces;

public interface IEntity
{
    Guid Id { get; set; }
    DateTime CreatedAt { get; set; }
}
