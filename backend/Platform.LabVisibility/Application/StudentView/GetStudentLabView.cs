using Platform.Access.Contracts;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.LabVisibility.Contracts;
using Platform.LabVisibility.Domain;

namespace Platform.LabVisibility.Application.StudentView;

public sealed class GetStudentLabView(IAccessDecision accessDecision, ILabPublicContentReader labs, ILabVisibilityAudit audit)
{
    public async ValueTask<StudentLabView?> ExecuteAsync(Guid labId, SubjectRef subject, string correlationId, CancellationToken cancellationToken = default)
    {
        var request = new AccessRequest(subject, LabVisibilityActions.StudentView, new ResourceRef("lab", labId.ToString("D")), correlationId);

        var decision = await accessDecision.DecideAsync(request, cancellationToken);

        StudentLabView? view = null;

        if (decision.Outcome == AccessOutcome.Allow)
        {
            var content = await labs.GetAsync(labId, cancellationToken);

            if (content is not null && content.State == LabVisibilityState.Published)
            {
                view = StudentViewProjector.Project(content);
            }
        }

        var outcome = "Denied";

        if (view is not null)
        {
            outcome = "Allowed";
        }

        await audit.WriteAsync(new LabVisibilityAuditEvent(LabVisibilityActions.StudentView, labId, subject.Id, outcome, correlationId), cancellationToken);

        return view;
    }
}