
using Platform.Api.Modules.Laboratories.Api;

namespace Platform.Api.Modules.Laboratories.Application;

public static class CatalogFilterService
{
    public static IReadOnlyList<CatalogLaboratoryResponse> Apply(
        IEnumerable<CatalogLaboratoryResponse> laboratories,
        CatalogLaboratoryFilter filter)
    {
        ArgumentNullException.ThrowIfNull(laboratories);
        ArgumentNullException.ThrowIfNull(filter);

        var query = laboratories.AsEnumerable();

        // Search by laboratory title or objective.
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var search = filter.Search.Trim();

            query = query.Where(lab =>
                lab.Title.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                lab.Objective.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        // Filter by difficulty.
        if (!string.IsNullOrWhiteSpace(filter.Difficulty))
        {
            query = query.Where(lab =>
                string.Equals(
                    lab.Difficulty,
                    filter.Difficulty.Trim(),
                    StringComparison.OrdinalIgnoreCase));
        }

        // Filter by laboratory type.
        if (!string.IsNullOrWhiteSpace(filter.Type))
        {
            query = query.Where(lab =>
                string.Equals(
                    lab.Type,
                    filter.Type.Trim(),
                    StringComparison.OrdinalIgnoreCase));
        }

        // Filter by tag.
        if (!string.IsNullOrWhiteSpace(filter.Tag))
        {
            var tag = filter.Tag.Trim();

            query = query.Where(lab =>
                lab.Tags.Any(t =>
                    string.Equals(
                        t,
                        tag,
                        StringComparison.OrdinalIgnoreCase)));
        }

        return query.ToArray();
    }
}
