namespace Platform.LabVisibility.Contracts;

public static class LabVisibilityActions
{
    public const string StudentView = "lab.student.view";
    public const string Preview = "lab.preview";
    public const string KeyRead = "lab.key.read";

    // KT2 (kartica 5, Gajić): upis evaluator ključa za nacrt. Sme samo nastavnik kursa, samo dok je nacrt.
    public const string KeyWrite = "lab.key.write";

    // #185 (ACC-05 strogi režim): ulazna radnja na HTTP rutama /student-view i /preview.
    // Centralni middleware njome proverava samo aktivnu sesiju; stvarnu odluku (kurs, objava,
    // nastavnik) i dalje donose StudentView/Preview politike u servisima, da odbijeno ostane 404.
    // Namerno nije u All: nije radnja nad laboratorijom.
    public const string Entry = "lab.visibility.entry";

    // KT2 (AUT-07-2, Cvetojević): promena stanja objave. Nisu radnje pristupa tima 3 (stanje javlja
    // vlasnik objave), ali su u All da bi ih trajni audit (LabVisibilityAuditMapping) prepoznao i upisao.
    public const string Publish = "lab.publication.publish";
    public const string Archive = "lab.publication.archive";

    public static readonly IReadOnlyList<string> All = [StudentView, Preview, KeyRead, KeyWrite, Publish, Archive];
}
