using System;
using System.Collections.Generic;
using System.Text;
using Platform.Prerequisites.Contracts;
using Platform.Prerequisites.Domain;

namespace Platform.Prerequisites.Application;

public sealed class PrerequisiteAccessService : IPrerequisiteAccessService
{
    private readonly IPrerequisiteRepository _prerequisiteRepository;
    private readonly ICompletedLaboratories _completedLaboratories;

    public PrerequisiteAccessService(
        IPrerequisiteRepository prerequisiteRepository,
        ICompletedLaboratories completedLaboratories)
    {
        _prerequisiteRepository = prerequisiteRepository;
        _completedLaboratories = completedLaboratories;
    }

    public async Task<PrerequisiteAccessResult> CheckAccessAsync(
        Guid studentId,
        Guid laboratoryId,
        CancellationToken cancellationToken = default)
    {
        if (studentId == Guid.Empty)
        {
            throw new ArgumentException(
                "Student ID cannot be empty.",
                nameof(studentId));
        }

        if (laboratoryId == Guid.Empty)
        {
            throw new ArgumentException(
                "Laboratory ID cannot be empty.",
                nameof(laboratoryId));
        }

        IReadOnlyList<LabPrerequisite> prerequisites =
            await _prerequisiteRepository.GetForLaboratoryAsync(
                laboratoryId,
                cancellationToken);

        IReadOnlySet<Guid> completedLaboratories =
            await _completedLaboratories.GetCompletedAsync(
                studentId,
                cancellationToken);

        List<Guid> missingPrerequisites = prerequisites
            .Where(prerequisite =>
                !completedLaboratories.Contains(prerequisite.RequiredLaboratoryId))
            .Select(prerequisite => prerequisite.RequiredLaboratoryId)
            .ToList();

        return new PrerequisiteAccessResult(
            missingPrerequisites.Count == 0,
            missingPrerequisites);
    }
}
