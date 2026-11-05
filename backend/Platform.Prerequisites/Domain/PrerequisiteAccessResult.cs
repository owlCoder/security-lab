using System;
using System.Collections.Generic;
using System.Text;

namespace Platform.Prerequisites.Domain;

public sealed record PrerequisiteAccessResult(
    bool IsUnlocked,
    IReadOnlyList<Guid> MissingPrerequisites);
