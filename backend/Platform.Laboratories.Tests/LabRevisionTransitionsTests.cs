using NUnit.Framework;
using Platform.Laboratories.Domain.Enums;
using Platform.Laboratories.Domain.Rules;

namespace Platform.Laboratories.Tests;

public class LabRevisionTransitionsTests
{
    [TestCase(LabRevisionStatus.Draft, LabRevisionStatus.Draft, false)]
    [TestCase(LabRevisionStatus.Draft, LabRevisionStatus.InReview, true)]
    [TestCase(LabRevisionStatus.Draft, LabRevisionStatus.Published, false)]
    [TestCase(LabRevisionStatus.Draft, LabRevisionStatus.Archived, false)]

    [TestCase(LabRevisionStatus.InReview, LabRevisionStatus.Draft, true)]
    [TestCase(LabRevisionStatus.InReview, LabRevisionStatus.InReview, false)]
    [TestCase(LabRevisionStatus.InReview, LabRevisionStatus.Published, true)]
    [TestCase(LabRevisionStatus.InReview, LabRevisionStatus.Archived, false)]

    [TestCase(LabRevisionStatus.Published, LabRevisionStatus.Draft, false)]
    [TestCase(LabRevisionStatus.Published, LabRevisionStatus.InReview, false)]
    [TestCase(LabRevisionStatus.Published, LabRevisionStatus.Published, false)]
    [TestCase(LabRevisionStatus.Published, LabRevisionStatus.Archived, true)]

    [TestCase(LabRevisionStatus.Archived, LabRevisionStatus.Draft, false)]
    [TestCase(LabRevisionStatus.Archived, LabRevisionStatus.InReview, false)]
    [TestCase(LabRevisionStatus.Archived, LabRevisionStatus.Published, false)]
    [TestCase(LabRevisionStatus.Archived, LabRevisionStatus.Archived, false)]
    public void CanTransition_ReturnsExpectedResult(
        LabRevisionStatus from,
        LabRevisionStatus to,
        bool expected)
    {
        var result = LabRevisionTransitions.CanTransition(from, to);

        Assert.That(result, Is.EqualTo(expected));
    }
}