namespace Platform.AssignmentPrerequisites.Domain;

public static class PrerequisiteGraph
{
    public static bool IsDuplicate(
        IEnumerable<AssignmentPrerequisite> existing,
        AssignmentPrerequisite candidate)
    {
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(candidate);

        return existing.Any(candidate.IsSameRelation);
    }

    public static bool WouldCreateCycle(
        IReadOnlyCollection<AssignmentPrerequisite> existing,
        AssignmentPrerequisite candidate)
    {
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(candidate);

        ILookup<Guid, Guid> requiredBy = existing
            .Where(p => string.Equals(p.CourseId, candidate.CourseId, StringComparison.Ordinal))
            .ToLookup(p => p.AssignmentId, p => p.RequiredAssignmentId);

        var visited = new HashSet<Guid>();
        var toVisit = new Stack<Guid>();
        toVisit.Push(candidate.RequiredAssignmentId);

        while (toVisit.Count > 0)
        {
            Guid current = toVisit.Pop();

            if (current == candidate.AssignmentId)
            {
                return true;
            }

            if (!visited.Add(current))
            {
                continue;
            }

            foreach (Guid next in requiredBy[current])
            {
                toVisit.Push(next);
            }
        }

        return false;
    }
}
