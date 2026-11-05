using Platform.Laboratories.Domain.Enums;


namespace Platform.Laboratories.Domain.Rules
{
    public static class LabRevisionTransitions
    {
        public static bool CanTransition(LabRevisionStatus from, LabRevisionStatus to)
        {
            return (from, to) switch
            {
                (LabRevisionStatus.Draft, LabRevisionStatus.InReview) => true,
                (LabRevisionStatus.InReview, LabRevisionStatus.Draft) => true,
                (LabRevisionStatus.InReview, LabRevisionStatus.Published) => true,
                (LabRevisionStatus.Published, LabRevisionStatus.Archived) => true,
                _ => false
            };
        }
    }
}
