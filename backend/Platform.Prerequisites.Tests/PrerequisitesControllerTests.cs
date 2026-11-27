using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NUnit.Framework;
using Platform.Access.Contracts;
using Platform.Access.Domain.Models;
using Platform.Api.Controllers;
using Platform.Api.Interfaces.Identity;
using Platform.Api.Modules.Laboratories.Domain;
using Platform.Api.Modules.Laboratories.Infrastructure;
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
            new UnauthenticatedSubjectResolver(),
            new InMemoryLaboratoryRepository());

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
            new AuthenticatedSubjectResolver(),
            new InMemoryLaboratoryRepository());

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


    [Test]
    public async Task Get_StorageUnavailable_Returns503()
    {
        var controller = CreateControllerWithUnavailableStorage();

        var result = await controller.Get(
            Guid.NewGuid(),
            CancellationToken.None);

        AssertStorageUnavailable(result);
    }

    [Test]
    public async Task Add_StorageUnavailable_Returns503()
    {
        var laboratories = new InMemoryLaboratoryRepository();

        var requiredLaboratory = new Laboratory(
            Guid.NewGuid(),
            "Required Laboratory",
            Guid.NewGuid());

        await laboratories.AddAsync(requiredLaboratory);

        var controller = new PrerequisitesController(
            new UnavailablePrerequisiteRepository(),
            new AllowAccessDecision(),
            new AuthenticatedSubjectResolver(),
            laboratories);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };

        var result = await controller.Add(
            Guid.NewGuid(),
            new PrerequisitesController.AddPrerequisiteRequest(
                requiredLaboratory.Id),
            CancellationToken.None);

        AssertStorageUnavailable(result);
    }

    [Test]
    public async Task Remove_StorageUnavailable_Returns503()
    {
        var controller = CreateControllerWithUnavailableStorage();

        var result = await controller.Remove(
            Guid.NewGuid(),
            Guid.NewGuid(),
            CancellationToken.None);

        AssertStorageUnavailable(result);
    }

    private static PrerequisitesController CreateControllerWithUnavailableStorage()
    {
        var controller = new PrerequisitesController(
            new UnavailablePrerequisiteRepository(),
            new AllowAccessDecision(),
            new AuthenticatedSubjectResolver(),
            new InMemoryLaboratoryRepository());

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };

        return controller;
    }

    private static void AssertStorageUnavailable(IActionResult result)
    {
        Assert.That(result, Is.TypeOf<ObjectResult>());

        var response = (ObjectResult)result;

        Assert.That(response.StatusCode, Is.EqualTo(503));
    }

    private sealed class AllowAccessDecision
        : Platform.Access.Contracts.IAccessDecision
    {
        public ValueTask<AccessDecision> DecideAsync(
            AccessRequest request,
            CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(
                AccessDecision.Allow(
                    "prerequisites-test-policy",
                    request.CorrelationId));
        }
    }


    [Test]
    public async Task Remove_SelfPrerequisite_Returns400()
    {
        var controller = CreateControllerWithUnavailableStorage();
        var laboratoryId = Guid.NewGuid();

        var result = await controller.Remove(
            laboratoryId,
            laboratoryId,
            CancellationToken.None);

        Assert.That(result, Is.TypeOf<BadRequestObjectResult>());

        var response = (BadRequestObjectResult)result;
        Assert.That(response.StatusCode, Is.EqualTo(400));
    }


}