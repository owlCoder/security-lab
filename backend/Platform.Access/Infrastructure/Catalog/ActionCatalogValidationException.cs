namespace Platform.Access.Infrastructure.Catalog;

public sealed class ActionCatalogValidationException(string message) : InvalidOperationException(message);
