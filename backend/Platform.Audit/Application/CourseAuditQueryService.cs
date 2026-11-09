using Platform.Audit.Contracts;

namespace Platform.Audit.Application;

/// <summary>
/// Reads the rows of one course page. The course filter is part of every query; an
/// implementation must never offer a read without it (T07). Returns at most
/// <c>PageSize + 1</c> rows so the caller can tell whether a next page exists.
/// </summary>
internal interface ICourseAuditEventReader
{
    Task<IReadOnlyList<CourseAuditEventDto>> ReadPageAsync(
        NormalizedCourseAuditQuery query, CancellationToken cancellationToken = default);
}

/// <summary>
/// Course audit query (AUD-06, S-07): normalize filters, read one page of one course, map to the
/// allowlisted DTO. No authorization here; see <see cref="ICourseAuditQueryService"/>.
/// </summary>
internal sealed class CourseAuditQueryService(CourseAuditQueryNormalizer normalizer, ICourseAuditEventReader reader)
    : ICourseAuditQueryService
{
    private readonly CourseAuditQueryNormalizer _normalizer =
        normalizer ?? throw new ArgumentNullException(nameof(normalizer));

    private readonly ICourseAuditEventReader _reader = reader ?? throw new ArgumentNullException(nameof(reader));

    public async Task<CourseAuditQueryResult> QueryAsync(
        CourseAuditQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var errors = _normalizer.Normalize(query, out var normalized);
        if (normalized is null)
            return CourseAuditQueryResult.Invalid(errors);

        IReadOnlyList<CourseAuditEventDto> rows;
        try
        {
            rows = await _reader.ReadPageAsync(normalized, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // No exception text reaches the caller: it can contain SQL or connection details.
            return CourseAuditQueryResult.Unavailable;
        }

        // Trimming depends only on the extra row. The flag also requires that the next page passes the
        // offset limit, otherwise it would announce a page the normalizer refuses as OutOfRange.
        var hasMoreRows = rows.Count > normalized.PageSize;
        var items = hasMoreRows ? rows.Take(normalized.PageSize).ToArray() : rows;
        var hasNextPage = hasMoreRows && normalized.NextPageIsAllowed;

        return CourseAuditQueryResult.Ok(new CourseAuditPage(
            normalized.CourseId, normalized.From, normalized.To, normalized.Page, normalized.PageSize,
            hasNextPage, items));
    }
}
