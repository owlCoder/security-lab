using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NUnit.Framework;
using Platform.Access.Contracts;
using Platform.Access.Domain.Models;
using Platform.Api.Controllers;
using Platform.Api.Interfaces.Identity;
using Platform.Prerequisites.Infrastructure;

namespace Platform.Prerequisites.Tests;

[TestFixture]
public class PrerequisitesControllerTests
{
    [Test]
    public async Task Add_UnauthorizedUser_Returns403()
    {
        var controller = CreateController();

        var result = await controller.Add(
            Guid.NewGuid(),
            new PrerequisitesController.AddPrerequisiteRequest(
                Guid.NewGuid()),
            CancellationToken.None);

        AssertForbidden(result);
    }

    [Test]
    public async Task Remove_UnauthorizedUser_Returns403()
    {
        var controller = CreateController();

        var result = await controller.Remove(
            Guid.NewGuid(),
            Guid.NewGuid(),
            CancellationToken.None);

        AssertForbidden(result);
    }

    private static PrerequisitesController CreateController()
    {
        var controller = new PrerequisitesController(
            new InMemoryPrerequisiteRepository(),
            new DenyAccessDecision(),
            new UnauthenticatedSubjectResolver());

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };

        return controller;
    }

    private static void AssertForbidden(IActionResult result)
    {
        Assert.That(result, Is.TypeOf<ObjectResult>());

        var response = (ObjectResult)result;

        Assert.That(response.StatusCode, Is.EqualTo(403));
    }

    private sealed class UnauthenticatedSubjectResolver
        : IRequestSubjectResolver
    {
        public SubjectRef? Resolve(HttpContext context)
        {
            return null;
        }
    }

    private sealed class DenyAccessDecision
    : Platform.Access.Contracts.IAccessDecision
    {
        public ValueTask<AccessDecision> DecideAsync(
            AccessRequest request,
            CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(
                AccessDecision.Deny(
                    default,
                    request.CorrelationId));
        }
    }
    [Test]
    public async Task Add_AuthenticatedButUnauthorized_Returns403()
    {
        var controller = CreateControllerWithDeniedPermission();

        var result = await controller.Add(
            Guid.NewGuid(),
            new PrerequisitesController.AddPrerequisiteRequest(Guid.NewGuid()),
            CancellationToken.None);

        AssertForbidden(result);
    }

    [Test]
    public async Task Remove_AuthenticatedButUnauthorized_Returns403()
    {
        var controller = CreateControllerWithDeniedPermission();

        var result = await controller.Remove(
            Guid.NewGuid(),
            Guid.NewGuid(),
            CancellationToken.None);

        AssertForbidden(result);
    }

    private static PrerequisitesController CreateControllerWithDeniedPermission()
    {
        var controller = new PrerequisitesController(
            new InMemoryPrerequisiteRepository(),
            new DenyAccessDecision(),
            new AuthenticatedSubjectResolver());

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };

        return controller;
    }

    private sealed class AuthenticatedSubjectResolver
        : IRequestSubjectResolver
    {
        public SubjectRef? Resolve(HttpContext context)
        {
            return new SubjectRef(
                Guid.NewGuid().ToString(),
                Platform.Access.Domain.Enums.AccountState.Active);
        }
    }
}