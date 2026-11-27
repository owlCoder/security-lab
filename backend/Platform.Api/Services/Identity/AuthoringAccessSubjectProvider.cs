using Platform.Access.AspNet;
using Platform.Access.Domain.Models;
using Platform.Api.Interfaces.Identity;
using Platform.Authoring.Application;

namespace Platform.Api.Services.Identity;

internal sealed class AuthoringAccessSubjectProvider(IAccessSubjectProvider previous, IRequestSubjectResolver sessionResolver) : IAccessSubjectProvider
{
    public static bool AppliesTo(HttpContext context)
    {
        var action = context.GetEndpoint()?.Metadata.GetMetadata<IAccessRequirementMetadata>()?.Action;
        return action is AuthoringActions.TemplateList or AuthoringActions.TemplateCopy;
    }

    public ValueTask<SubjectRef?> GetSubjectAsync(HttpContext context, CancellationToken cancellationToken = default)
    {
        if(AppliesTo(context))
        {
            //Use the same validated session as the Authoring handlers
            return ValueTask.FromResult(sessionResolver.Resolve(context));
        }

        return previous.GetSubjectAsync(context, cancellationToken);
    }

    public static void Register(IServiceCollection services)
    {
        var previous = services.Last(descriptor => descriptor.ServiceType == typeof(IAccessSubjectProvider) && !descriptor.IsKeyedService);

        //Capture the existing factory so other routes keep their subject provider
        var createPrevious = previous.ImplementationFactory ?? throw new InvalidOperationException("Register revision request identity before Authoring request identity");

        services.Remove(previous);
        services.AddScoped<IAccessSubjectProvider>(provider => new AuthoringAccessSubjectProvider((IAccessSubjectProvider)createPrevious(provider), provider.GetRequiredKeyedService<IRequestSubjectResolver>("AuthoringSession")));
    }
}