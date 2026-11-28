namespace Platform.Api.Services.Identity;

public sealed class EmailDeliveryException(string message, Exception? innerException = null)
    : Exception(message, innerException);
