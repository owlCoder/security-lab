namespace Platform.LabVisibility.Contracts;

public static class LabVisibilityActions
{
    public const string StudentView = "lab.student.view";
    public const string Preview = "lab.preview";
    public const string KeyRead = "lab.key.read";

    // #185 (ACC-05 strogi režim): ulazna radnja na HTTP rutama /student-view i /preview.
    // Centralni middleware njome proverava samo aktivnu sesiju; stvarnu odluku (kurs, objava,
    // nastavnik) i dalje donose StudentView/Preview politike u servisima, da odbijeno ostane 404.
    // Namerno nije u All: nije radnja nad laboratorijom.
    public const string Entry = "lab.visibility.entry";

    public static readonly IReadOnlyList<string> All = [StudentView, Preview, KeyRead];
}
