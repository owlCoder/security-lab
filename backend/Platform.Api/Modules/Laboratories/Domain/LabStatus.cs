namespace Platform.Api.Modules.Laboratories.Domain;

// Laboratory lifecycle only; publication and immutable revisions belong to the REV module.
public enum LabStatus
{
    Draft,
    Archived
}
