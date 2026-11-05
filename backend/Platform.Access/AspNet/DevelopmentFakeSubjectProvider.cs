using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;

namespace Platform.Access.AspNet;

public sealed class DevelopmentFakeSubjectProvider
    : IAccessSubjectProvider
{
    private readonly IWebHostEnvironment _environment;

    public DevelopmentFakeSubjectProvider(
        IWebHostEnvironment environment)
    {
        _environment = environment;
    }

    public ValueTask<SubjectRef?> GetSubjectAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken = default)
    {
        if (!_environment.IsDevelopment())
        {
            return ValueTask.FromResult<SubjectRef?>(null);
        }

        var subject = new SubjectRef(
            Id: "dev-user",
            State: AccountState.Active
        );

        return ValueTask.FromResult<SubjectRef?>(subject);
    }
}