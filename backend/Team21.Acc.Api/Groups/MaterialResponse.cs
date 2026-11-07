using Team21.Acc.Core.Groups;

namespace Team21.Acc.Api.Groups;

/// <summary>A material the caller may see. Only its reference, the content belongs to its owner module.</summary>
public sealed record MaterialResponse(string Type, Guid MaterialId)
{
    public static MaterialResponse From(MaterialRef material) =>
        new(material.Type.ToString(), material.MaterialId);
}
