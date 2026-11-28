namespace Platform.LabVisibility.Contracts;

// AUT-07 (KT2): za objavu treba samo da znamo za koje zadatke ključ postoji, ne i odgovore.
// Zato poseban, uzak port: vraća ID-jeve zadataka i nikad ne čita kolonu sa odgovorom
// (model pretnji: ključ izlazi samo kroz EvaluatorKeyReader).
public interface IEvaluatorKeyCoverage
{
    ValueTask<IReadOnlySet<string>> GetCoveredTaskIdsAsync(
        Guid labId,
        CancellationToken cancellationToken = default);
}
