using System;
using System.Collections.Generic;
using System.Text;
using Platform.Prerequisites.Domain;

namespace Platform.Prerequisites.Contracts;

public interface IPrerequisiteAccessService
{
    Task<PrerequisiteAccessResult> CheckAccessAsync(
        Guid studentId,
        Guid laboratoryId,
        CancellationToken cancellationToken = default);
}
