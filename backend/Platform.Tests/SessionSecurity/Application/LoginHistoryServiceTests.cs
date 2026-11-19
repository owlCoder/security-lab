using Platform.SessionSecurity.Application;
using Platform.SessionSecurity.Contracts;
using Platform.SessionSecurity.Domain;
using Platform.SessionSecurity.Infrastructure;
using Platform.Tests.SessionSecurity.TestDoubles;

// IDN-22 TIM 09
namespace Platform.Tests.SessionSecurity.Application;

[TestFixture]
public sealed class LoginHistoryServiceTests
{
    private static readonly DateTimeOffset Now = SyntheticLoginEvents.Now;
    private static readonly AuthenticatedSubject UserA = new(SyntheticLoginEvents.UserA, IsAuthenticated: true);

    private RecordingLoginHistorySource _source = null!; // assigned in SetUp before every test
    private LoginHistoryService _service = null!; // assigned in SetUp before every test

    [SetUp]
    public void SetUp()
    {
        _source = new RecordingLoginHistorySource(
            new InMemoryLoginHistorySource(SyntheticLoginEvents.ForBothUsers()));
        _service = new LoginHistoryService(_source, new FixedTimeProvider(Now));
    }

    [Test]
    public async Task SearchAsync_WhenUserAHasEvents_ReturnsOnlyOwnEventsFromDefaultRange()
    {
        var result = await _service.SearchAsync(UserA, new SearchLoginHistoryQuery());

        Assert.Multiple(() =>
        {
            Assert.That(result.Code, Is.EqualTo(LoginHistoryCodes.Ok));
            Assert.That(
                result.Items.Select(item => item.OccurredAt),
                Is.EqualTo(new[] { Now.AddHours(-1), Now.AddDays(-2), Now.AddDays(-10) }));
        });
    }

    [Test]
    public async Task SearchAsync_WhenUserBHasEventsInSamePeriod_NeverReturnsThemToUserA()
    {
        var userBTimes = SyntheticLoginEvents.ForUserB().Select(loginEvent => loginEvent.OccurredAt);

        var result = await _service.SearchAsync(
            UserA, new SearchLoginHistoryQuery(FromUtc: Now - LoginHistoryLimits.MaxRange, ToUtc: Now));

        Assert.Multiple(() =>
        {
            Assert.That(result.Items.Select(item => item.OccurredAt).Intersect(userBTimes), Is.Empty);
            Assert.That(_source.RequestedSubjectIds, Is.EqualTo(new[] { SyntheticLoginEvents.UserA }));
        });
    }

    [Test]
    public async Task SearchAsync_WhenPeriodAndOutcomeAreGiven_ReturnsExactSubset()
    {
        var query = new SearchLoginHistoryQuery(
            FromUtc: Now.AddDays(-3), ToUtc: Now, Outcome: LoginOutcomeFilter.Failed);

        var result = await _service.SearchAsync(UserA, query);

        Assert.That(
            result.Items,
            Is.EqualTo(new[] { new LoginHistoryItem(Now.AddDays(-2), Succeeded: false, "198.51.100.*") }));
    }

    [Test]
    public async Task SearchAsync_WhenEventsHaveAddresses_ReturnsOnlyMaskedAddresses()
    {
        var result = await _service.SearchAsync(UserA, new SearchLoginHistoryQuery());

        Assert.That(
            result.Items.Select(item => item.MaskedSourceAddress),
            Is.EqualTo(new[] { "203.0.113.*", "198.51.100.*", "2001:db8:85a3:8d3::*" }));
    }

    [Test]
    public async Task SearchAsync_WhenResultIsReturned_ContainsNoFullAddressOrSubjectId()
    {
        var result = await _service.SearchAsync(
            UserA, new SearchLoginHistoryQuery(FromUtc: Now - LoginHistoryLimits.MaxRange, ToUtc: Now));
        var text = string.Join('|', result.Items.Select(item => item.ToString()));

        Assert.Multiple(() =>
        {
            var fullAddresses = SyntheticLoginEvents.ForUserA()
                .Select(loginEvent => loginEvent.SourceAddress)
                .OfType<string>();

            foreach (var fullAddress in fullAddresses)
            {
                Assert.That(text, Does.Not.Contain(fullAddress));
            }

            Assert.That(text, Does.Not.Contain(SyntheticLoginEvents.UserA));
        });
    }

    [TestCaseSource(nameof(InvalidQueries))]
    public async Task SearchAsync_WhenQueryIsInvalid_RejectsWithCodeAndDoesNotCallSource(
        SearchLoginHistoryQuery query, string expectedCode)
    {
        var result = await _service.SearchAsync(UserA, query);

        AssertRejectedWithoutSourceCall(result, expectedCode);
    }

    [Test]
    public async Task SearchAsync_WhenRangeIsExactlyMaximumAndPageSizeIsMaximum_IsAccepted()
    {
        var query = new SearchLoginHistoryQuery(
            FromUtc: Now - LoginHistoryLimits.MaxRange, ToUtc: Now, PageSize: LoginHistoryLimits.MaxPageSize);

        var result = await _service.SearchAsync(UserA, query);

        Assert.Multiple(() =>
        {
            Assert.That(result.Code, Is.EqualTo(LoginHistoryCodes.Ok));
            Assert.That(result.PageSize, Is.EqualTo(LoginHistoryLimits.MaxPageSize));
            Assert.That(_source.CallCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task SearchAsync_WhenSubjectIsNull_ReturnsNotAuthenticatedAndDoesNotCallSource()
    {
        var result = await _service.SearchAsync(null, new SearchLoginHistoryQuery());

        AssertRejectedWithoutSourceCall(result, LoginHistoryCodes.NotAuthenticated);
    }

    [Test]
    public async Task SearchAsync_WhenSubjectIsNotAuthenticated_ReturnsNotAuthenticatedAndDoesNotCallSource()
    {
        var subject = new AuthenticatedSubject(SyntheticLoginEvents.UserA, IsAuthenticated: false);

        var result = await _service.SearchAsync(subject, new SearchLoginHistoryQuery());

        AssertRejectedWithoutSourceCall(result, LoginHistoryCodes.NotAuthenticated);
    }

    [TestCase("")]
    [TestCase("   ")]
    public async Task SearchAsync_WhenSubjectIdIsEmpty_ReturnsInvalidRequestAndDoesNotCallSource(string subjectId)
    {
        var subject = new AuthenticatedSubject(subjectId, IsAuthenticated: true);

        var result = await _service.SearchAsync(subject, new SearchLoginHistoryQuery());

        AssertRejectedWithoutSourceCall(result, LoginHistoryCodes.InvalidRequest);
    }

    [Test]
    public async Task SearchAsync_WhenQueryIsNull_ReturnsInvalidRequestAndDoesNotCallSource()
    {
        var result = await _service.SearchAsync(UserA, null);

        AssertRejectedWithoutSourceCall(result, LoginHistoryCodes.InvalidRequest);
    }

    [Test]
    public async Task SearchAsync_WhenValidFilterHasNoMatches_ReturnsOkWithEmptyList()
    {
        var query = new SearchLoginHistoryQuery(FromUtc: Now.AddDays(-80), ToUtc: Now.AddDays(-60));

        var result = await _service.SearchAsync(UserA, query);

        Assert.Multiple(() =>
        {
            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.Code, Is.EqualTo(LoginHistoryCodes.Ok));
            Assert.That(result.Items, Is.Empty);
        });
    }

    [Test]
    public async Task SearchAsync_WhenSecondPageIsRequested_DoesNotRepeatItemsOfFirstPage()
    {
        var from = Now - LoginHistoryLimits.MaxRange;

        var firstPage = await _service.SearchAsync(
            UserA, new SearchLoginHistoryQuery(FromUtc: from, ToUtc: Now, Page: 1, PageSize: 2));
        var secondPage = await _service.SearchAsync(
            UserA, new SearchLoginHistoryQuery(FromUtc: from, ToUtc: Now, Page: 2, PageSize: 2));

        Assert.Multiple(() =>
        {
            Assert.That(firstPage.Items, Has.Count.EqualTo(2));
            Assert.That(secondPage.Items, Has.Count.EqualTo(2));
            Assert.That(secondPage.Items.Intersect(firstPage.Items), Is.Empty);
            Assert.That(secondPage.Page, Is.EqualTo(2));
        });
    }

    [Test]
    public async Task SearchAsync_WhenNothingIsGiven_AsksSourceForDefaultWindow()
    {
        await _service.SearchAsync(UserA, new SearchLoginHistoryQuery());

        Assert.Multiple(() =>
        {
            Assert.That(_source.LastFromUtc, Is.EqualTo(Now - LoginHistoryLimits.DefaultRange));
            Assert.That(_source.LastToUtc, Is.EqualTo(Now));
            Assert.That(_source.LastSkip, Is.Zero);
            Assert.That(_source.LastTake, Is.EqualTo(LoginHistoryLimits.DefaultPageSize));
        });
    }

    [Test]
    public void Constructor_WhenDependencyIsNull_Throws()
    {
        // null! is intentional: the test checks the guard against a missing dependency.
        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentNullException>(() => _ = new LoginHistoryService(null!, TimeProvider.System));
            Assert.Throws<ArgumentNullException>(() => _ = new LoginHistoryService(_source, null!));
        });
    }

    private static IEnumerable<TestCaseData> InvalidQueries()
    {
        yield return new TestCaseData(
            new SearchLoginHistoryQuery(FromUtc: Now, ToUtc: Now.AddDays(-1)), LoginHistoryCodes.InvalidTimeRange)
            .SetName("SearchAsync_WhenFromIsAfterTo_ReturnsInvalidTimeRange");
        yield return new TestCaseData(
            new SearchLoginHistoryQuery(FromUtc: Now.AddDays(-91), ToUtc: Now), LoginHistoryCodes.RangeTooLarge)
            .SetName("SearchAsync_WhenRangeIsLongerThan90Days_ReturnsRangeTooLarge");
        yield return new TestCaseData(new SearchLoginHistoryQuery(Page: 0), LoginHistoryCodes.InvalidPage)
            .SetName("SearchAsync_WhenPageIsZero_ReturnsInvalidPage");
        yield return new TestCaseData(new SearchLoginHistoryQuery(PageSize: 0), LoginHistoryCodes.InvalidPageSize)
            .SetName("SearchAsync_WhenPageSizeIsZero_ReturnsInvalidPageSize");
        yield return new TestCaseData(
            new SearchLoginHistoryQuery(PageSize: LoginHistoryLimits.MaxPageSize + 1), LoginHistoryCodes.InvalidPageSize)
            .SetName("SearchAsync_WhenPageSizeIs51_ReturnsInvalidPageSize");
        yield return new TestCaseData(
            new SearchLoginHistoryQuery(Outcome: (LoginOutcomeFilter)42), LoginHistoryCodes.InvalidRequest)
            .SetName("SearchAsync_WhenOutcomeIsNotDefined_ReturnsInvalidRequest");
    }

    private void AssertRejectedWithoutSourceCall(LoginHistoryResult result, string expectedCode)
    {
        Assert.Multiple(() =>
        {
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Code, Is.EqualTo(expectedCode));
            Assert.That(result.Items, Is.Empty);
            Assert.That(_source.CallCount, Is.Zero);
        });
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
