using System.Text;
using Platform.Access.Domain.Constants;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Access.Domain.ValueObjects;
using Platform.Access.Tests.Support;
using NUnit.Framework;

namespace Platform.Access.Tests;

[TestFixture]
[Property("RequirementId", "ACC-05")]
public class ActionIdFuzzTests
{
    [TestCase("lab.read", true)]
    [TestCase("results.sensitive.read", true)]
    [TestCase("a.b", true)]
    [TestCase("lab", false)]
    [TestCase("Lab.read", false)]
    [TestCase("lab.read\n", false)]
    [TestCase("lab.read\0", false)]
    [TestCase("lab.1read", false)]
    [TestCase(".lab.read", false)]
    [TestCase("lab.read.", false)]
    [TestCase("lab-read.x", false)]
    public void ACC05_ActionId_StrictFormat(string value, bool expected) =>
        Assert.That(ActionId.IsValid(value), Is.EqualTo(expected));

    [Test]
    public void ACC05_ActionId_RejectsNullAndOverlongValues()
    {
        Assert.That(ActionId.IsValid(null), Is.False);
        Assert.That(ActionId.IsValid("a." + new string('b', 62)), Is.True);   // tačno 64
        Assert.That(ActionId.IsValid("a." + new string('b', 63)), Is.False);  // 65
    }

    [Test]
    public async Task ACC05_RandomActionStrings_NeverAllowedUnlessExactlyRegistered()
    {
        var fx = new EngineFixture().WithPermitAllPolicies();
        var engine = fx.Engine();
        var registered = AccessActions.All.Select(d => d.Id).ToHashSet(StringComparer.Ordinal);
        var random = new Random(20260511);
        var states = new[] { AccountState.Active, AccountState.Blocked, AccountState.Archived };

        for (var i = 0; i < 3_000; i++)
        {
            var candidate = i % 5 == 0
                ? Mutate(registered.ElementAt(random.Next(registered.Count)), random)
                : RandomString(random);
            var state = states[random.Next(states.Length)];

            var decision = await engine.DecideAsync(
                AccessRequestBuilder.Default().WithAction(candidate).WithState(state).Build());

            var isPublic = AccessActions.All.Any(d => d.Id == candidate && d.Scope == ActionScope.Public);
            var expectedAllow = registered.Contains(candidate) && (state == AccountState.Active || isPublic);
            Assert.That(decision.Outcome == AccessOutcome.Allow, Is.EqualTo(expectedAllow),
                $"Unexpected outcome for candidate of length {candidate.Length} (iteration {i}).");
        }
    }

    private static string RandomString(Random random)
    {
        var length = random.Next(0, 90);
        var builder = new StringBuilder(length);
        for (var i = 0; i < length; i++)
        {
            builder.Append(random.Next(6) switch
            {
                0 => (char)random.Next(0x00, 0x20),
                1 => (char)random.Next(0x61, 0x7B),
                2 => '.',
                3 => (char)random.Next(0x41, 0x5B),
                4 => (char)random.Next(0xFF00, 0xFFEF),
                _ => (char)random.Next(0x20, 0x7F),
            });
        }

        return builder.ToString();
    }

    private static string Mutate(string valid, Random random) => random.Next(5) switch
    {
        0 => valid + "\n",
        1 => valid.ToUpperInvariant(),
        2 => " " + valid,
        3 => valid + "\0",
        _ => valid.Insert(random.Next(valid.Length + 1), "."),
    };
}
