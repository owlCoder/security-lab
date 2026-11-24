namespace Platform.Access.Domain.Models;

public readonly record struct AttributeValue(bool IsPresent, object? Value)
{
    public static AttributeValue Missing => default;

    public static AttributeValue Of(object value) => new(true, value);
}
