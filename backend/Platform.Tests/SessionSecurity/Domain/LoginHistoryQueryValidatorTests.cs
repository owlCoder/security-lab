using Platform.SessionSecurity.Domain;

// IDN-22 TIM 09
namespace Platform.Tests.SessionSecurity.Domain;

[TestFixture]
public sealed class LoginHistoryQueryValidatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 9, 0, 0, TimeSpan.Zero);

    [Test]
    public void Validate_WhenFromIsAfterTo_ReturnsInvalidTimeRange()
    {
        var result = Validate(fromUtc: Now, toUtc: Now.AddTicks(-1));

        AssertRejected(result, LoginHistoryCodes.InvalidTimeRange);
    }

    [Test]
    public void Validate_WhenOnlyFromIsGivenAndItIsInTheFuture_ReturnsInvalidTimeRange()
    {
        var result = Validate(fromUtc: Now.AddDays(1));

        AssertRejected(result, LoginHistoryCodes.InvalidTimeRange);
    }

    [Test]
    public void Validate_WhenOnlyToIsGivenAndDefaultStartWouldUnderflow_ReturnsInvalidTimeRange()
    {
        var result = Validate(toUtc: DateTimeOffset.MinValue);

        AssertRejected(result, LoginHistoryCodes.InvalidTimeRange);
    }

    [Test]
    public void Validate_WhenRangeIsLongerThanMaximum_ReturnsRangeTooLarge()
    {
        var result = Validate(fromUtc: Now - LoginHistoryLimits.MaxRange - TimeSpan.FromTicks(1), toUtc: Now);

        AssertRejected(result, LoginHistoryCodes.RangeTooLarge);
    }

    [Test]
    public void Validate_WhenRangeIsExactlyMaximum_IsAccepted()
    {
        var result = Validate(fromUtc: Now - LoginHistoryLimits.MaxRange, toUtc: Now);

        Assert.That(result.IsValid, Is.True);
    }

    [TestCase(0)]
    [TestCase(-1)]
    [TestCase(int.MinValue)]
    public void Validate_WhenPageIsBelowFirstPage_ReturnsInvalidPage(int page)
    {
        var result = Validate(page: page);

        AssertRejected(result, LoginHistoryCodes.InvalidPage);
    }

    [Test]
    public void Validate_WhenSkipWouldOverflow_ReturnsInvalidPage()
    {
        var result = Validate(page: int.MaxValue, pageSize: LoginHistoryLimits.MaxPageSize);

        AssertRejected(result, LoginHistoryCodes.InvalidPage);
    }

    [TestCase(0)]
    [TestCase(-1)]
    [TestCase(LoginHistoryLimits.MaxPageSize + 1)]
    [TestCase(int.MaxValue)]
    public void Validate_WhenPageSizeIsOutOfLimits_ReturnsInvalidPageSize(int pageSize)
    {
        var result = Validate(pageSize: pageSize);

        AssertRejected(result, LoginHistoryCodes.InvalidPageSize);
    }

    [Test]
    public void Validate_WhenPageSizeIsExactlyMaximum_IsAccepted()
    {
        var result = Validate(pageSize: LoginHistoryLimits.MaxPageSize);

        Assert.That(result.IsValid, Is.True);
    }

    [Test]
    public void Validate_WhenOutcomeIsNotDefined_ReturnsInvalidRequest()
    {
        var result = Validate(outcome: (LoginOutcomeFilter)42);

        AssertRejected(result, LoginHistoryCodes.InvalidRequest);
    }

    [Test]
    public void Validate_WhenNothingIsGiven_UsesDefaultRangeAndFirstPage()
    {
        var result = Validate();

        Assert.Multiple(() =>
        {
            Assert.That(result.Code, Is.EqualTo(LoginHistoryCodes.Ok));
            Assert.That(result.Window?.ToUtc, Is.EqualTo(Now));
            Assert.That(result.Window?.FromUtc, Is.EqualTo(Now - LoginHistoryLimits.DefaultRange));
            Assert.That(result.Window?.Page, Is.EqualTo(LoginHistoryLimits.FirstPage));
            Assert.That(result.Window?.PageSize, Is.EqualTo(LoginHistoryLimits.DefaultPageSize));
            Assert.That(result.Window?.Skip, Is.Zero);
        });
    }

    [Test]
    public void Validate_WhenSecondPageIsRequested_SkipsOnePage()
    {
        var result = Validate(page: 2, pageSize: 10);

        Assert.That(result.Window?.Skip, Is.EqualTo(10));
    }

    [Test]
    public void Validate_WhenTimesHaveOffset_ReturnsUtcWindow()
    {
        var offset = TimeSpan.FromHours(2);

        var result = Validate(fromUtc: Now.AddDays(-1).ToOffset(offset), toUtc: Now.ToOffset(offset));

        Assert.Multiple(() =>
        {
            Assert.That(result.Window?.FromUtc.Offset, Is.EqualTo(TimeSpan.Zero));
            Assert.That(result.Window?.ToUtc.Offset, Is.EqualTo(TimeSpan.Zero));
        });
    }

    private static LoginHistoryQueryValidationResult Validate(
        DateTimeOffset? fromUtc = null,
        DateTimeOffset? toUtc = null,
        LoginOutcomeFilter? outcome = null,
        int? page = null,
        int? pageSize = null)
        => LoginHistoryQueryValidator.Validate(fromUtc, toUtc, outcome, page, pageSize, Now);

    private static void AssertRejected(LoginHistoryQueryValidationResult result, string expectedCode)
    {
        Assert.Multiple(() =>
        {
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Code, Is.EqualTo(expectedCode));
            Assert.That(result.Window, Is.Null);
        });
    }
}
