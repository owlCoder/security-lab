namespace Platform.Prerequisites.Domain;

public static class PrerequisiteRules
{
    public static bool IsDuplicate(
        IEnumerable<LabPrerequisite> existing,
        LabPrerequisite candidate)
    {
        return existing.Any(prerequisite =>
            prerequisite.LaboratoryId == candidate.LaboratoryId &&
            prerequisite.RequiredLaboratoryId == candidate.RequiredLaboratoryId);
    }

    public static bool WouldCreateCycle(
        IReadOnlyCollection<LabPrerequisite> existing,
        LabPrerequisite candidate)
    {
        var visited = new HashSet<Guid>();
        var toVisit = new Stack<Guid>();
        toVisit.Push(candidate.RequiredLaboratoryId);

        while (toVisit.Count > 0)
        {
            Guid current = toVisit.Pop();

            if (current == candidate.LaboratoryId)
            {
                return true;
            }

            if (!visited.Add(current))
            {
                continue;
            }

            foreach (LabPrerequisite prerequisite in existing)
            {
                if (prerequisite.LaboratoryId == current)
                {
                    toVisit.Push(prerequisite.RequiredLaboratoryId);
                }
            }
        }

        return false;
    }
}