using Team21.Acc.Core.Common.Interfaces;

namespace Team21.Acc.Core.Common;

/// <summary>
/// Base for all our tables. CreatedAt and UpdatedAt are filled on save, CreatedBy, UpdatedBy and
/// the soft delete fields are set in the service.
/// </summary>
public abstract class BaseEntity : IEntity, IUpdatable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public bool IsDeleted { get; set; } = false;
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }

    public Guid CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }
}
