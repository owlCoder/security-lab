namespace Platform.Api.Modules.LabLocalization;

public static class LabLocalizationActions
{
    // Middleware proverava samo aktivnu sesiju; pravo nad laboratorijom ili revizijom proverava ILocalizationAccess.
    public const string Entry = "lab.localization.entry";

    public const string Read = "lab.localization.read";
    public const string Translate = "lab.localization.translate";

    public const string LaboratoryResource = "laboratory";
    public const string RevisionResource = "revision";
}
