
using System.Text;
using Platform.Laboratories.Domain.Rules;

namespace Platform.Laboratories.Tests;

public class RevisionContentHashTests
{
    private const string AbcHash = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";

    [Test]
    public void Compute_KnownInput_MatchesSha256Vector()
    {
        var result = RevisionContentHash.Compute(Encoding.UTF8.GetBytes("abc"));
        Assert.That(result, Is.EqualTo(new RevisionContentHash.ComputationResult.Success(AbcHash)));
    }

    [Test]
    public void Compute_IdenticalBytes_ReturnsIdenticalHash()
    {
        var first = RevisionContentHash.Compute(Encoding.UTF8.GetBytes("snapshot"));
        var second = RevisionContentHash.Compute(Encoding.UTF8.GetBytes("snapshot"));
        Assert.That(first, Is.EqualTo(second));
    }

    [Test]
    public void Compute_ChangedBytes_ChangesHash()
    {
        var original = RevisionContentHash.Compute(Encoding.UTF8.GetBytes("abc"));
        var changed = RevisionContentHash.Compute(Encoding.UTF8.GetBytes("abd"));
        Assert.That(changed, Is.Not.EqualTo(original));
    }

    [Test]
    public void Compute_EmptySnapshot_ReturnsFailure()
    {
        var result = RevisionContentHash.Compute(ReadOnlySpan<byte>.Empty);
        Assert.That(result, Is.EqualTo(new RevisionContentHash.ComputationResult.Failure(
            RevisionContentHash.ComputationError.EmptySnapshot)));
    }

    [TestCase(AbcHash, true)]
    [TestCase("", false)]
    [TestCase(" ", false)]
    [TestCase("content-hash", false)]
    [TestCase("BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD", false)]
    [TestCase("ga7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", false)]
    public void IsValid_RequiresCanonicalLowercaseSha256(string hash, bool expected)
    {
        Assert.That(RevisionContentHash.IsValid(hash), Is.EqualTo(expected));
    }
}
