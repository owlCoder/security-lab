namespace Platform.Authoring.Application;

/// <summary>
/// Action names registered with the platform access module (Platform.Access).
/// They follow the lowercase dotted ActionId format. Anything not listed here is denied by default.
/// </summary>
public static class AuthoringActions
{
    public const string TemplateList = "authoring.template.list";
    public const string TemplateCopy = "authoring.template.copy";

    public static readonly IReadOnlyList<string> All = [TemplateList, TemplateCopy];

    /// <summary>Resource type used in access requests for a template.</summary>
    public const string TemplateResourceType = "lab-template";
}
