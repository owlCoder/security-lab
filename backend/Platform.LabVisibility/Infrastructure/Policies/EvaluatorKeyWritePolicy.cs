using Platform.Access.Contracts;
using Platform.Access.Domain.Models;

namespace Platform.LabVisibility.Infrastructure.Policies;

// lab.key.write: ključ sme da upiše samo nastavnik kursa te laboratorije, i samo dok je laboratorija nacrt.
// KT2 ugovor: politika je registrovana, ali do kartice 5 (Gajić) odbija sve zahteve (deny by default).
public sealed class EvaluatorKeyWritePolicy : IAccessPolicy
{
    public string Id => "lab-visibility.evaluator-key-write";

    public string Version => "v0";

    public ValueTask<bool> EvaluateAsync(
        AccessRequest request,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(false);
}
