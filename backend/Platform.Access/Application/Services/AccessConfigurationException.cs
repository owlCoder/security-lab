namespace Platform.Access.Application.Services;

public sealed class AccessConfigurationException(string message) : InvalidOperationException(message);
