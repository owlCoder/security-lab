
using System;
using System.Collections.Generic;

namespace Platform.Api.Modules.Laboratories.Api;

public sealed record CatalogLaboratoryResponse(
    Guid Id,
    string Title,
    string Status,
    string Objective,
    string Difficulty,
    string Type,
    IReadOnlyList<string> Tags
);

public sealed record CatalogLaboratoryFilter(
    string? Search,
    string? Difficulty,
    string? Type,
    string? Tag
);
