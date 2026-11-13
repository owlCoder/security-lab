using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Platform.Access.AspNet;
using Platform.Access.Domain.Constants;
using Team21.Acc.Api.Extensions;
using Team21.Acc.Core.Common;
using Team21.Acc.Core.Groups;
using Team21.Acc.Core.Groups.Visibility;
using Team21.Acc.Core.Groups.Visibility.Interfaces;

namespace Team21.Acc.Api.Groups;

/// <summary>
/// ACC-06 material visibility over HTTP. Every denial answers 404, same as a missing material.
/// </summary>
public static class MaterialEndpoints
{
    public static IEndpointRouteBuilder MapMaterialEndpoints(this IEndpointRouteBuilder app)
    {
        // ACC-05 checks for an active account, the service decides the rest
        app.MapGet("/api/materials/{type}/{id:guid}", GetMaterialAsync)
            .RequireAccess(AccessActions.MaterialRead.Id);
        app.MapGet("/api/courses/{courseId:guid}/materials", ListCourseMaterialsAsync)
            .RequireAccess(AccessActions.MaterialRead.Id);

        return app;
    }

    private static async Task<IResult> GetMaterialAsync(
        string type, Guid id, IMaterialVisibilityService visibility, CancellationToken ct)
    {
        // An unknown type goes in as null, the service answers 401 or 404 without a lookup
        var material = ParseType(type) is { } materialType
            ? new MaterialRef(materialType, id)
            : null;

        var result = await visibility.CanAccessAsync(material, MaterialAction.Read, ct);

        return result.IsSuccess && material is not null
            ? Results.Ok(MaterialResponse.From(material))
            : result.ToHttpResult();
    }

    private static async Task<IResult> ListCourseMaterialsAsync(
        Guid courseId, IMaterialVisibilityService visibility, CancellationToken ct)
    {
        var result = await visibility.FilterVisibleAsync(courseId, ct);
        if (result.IsFailure)
            return Result.Failure(result.Error!).ToHttpResult();

        return Results.Ok(result.Value!.Select(MaterialResponse.From).ToList());
    }

    // Names only, a number like "1" is not a valid type
    private static MaterialType? ParseType(string type) =>
        !int.TryParse(type, out _)
        && Enum.TryParse<MaterialType>(type, ignoreCase: true, out var parsed)
        && Enum.IsDefined(parsed)
            ? parsed
            : null;
}
