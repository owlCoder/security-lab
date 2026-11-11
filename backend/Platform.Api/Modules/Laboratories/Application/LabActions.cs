namespace Platform.Api.Modules.Laboratories.Application;

// Poznate radnje LAB modula. Radnja koja nije ovde navedena nema politiku i odbija se (ACC-05).
public static class LabActions
{
    public const string List = "laboratory.list";
    public const string Create = "laboratory.create";
    public const string Read = "laboratory.read";
    public const string Rename = "laboratory.rename";

    // #185: sve radnje koje rute modula prijavljuju centralnom ACC-05 middleware-u.
    public static IReadOnlyList<string> All { get; } = [List, Create, Read, Rename];
}