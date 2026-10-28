namespace Sentinel.Contracts.Primitives.Identifiers;

internal static class IdGuard
{
    internal const int MaxSlugLength = 63;
    internal const int MaxOpaqueLength = 256;

    internal static Guid NotEmpty(Guid value, string idName) =>
        value == Guid.Empty
            ? throw new ArgumentException($"{idName} cannot be empty.", nameof(value))
            : value;

    internal static string Slug(string value, string idName)
    {
        ArgumentNullException.ThrowIfNull(value);

        bool isValid = value.Length is > 0 and <= MaxSlugLength
            && IsLowercaseAlphanumeric(value[0])
            && IsLowercaseAlphanumeric(value[^1])
            && value.All(c => IsLowercaseAlphanumeric(c) || c == '-');

        return isValid
            ? value
            : throw new ArgumentException(
                $"{idName} must be 1-{MaxSlugLength} lowercase letters, digits or hyphens, starting and ending with a letter or digit.",
                nameof(value));

        static bool IsLowercaseAlphanumeric(char c) => c is >= 'a' and <= 'z' or >= '0' and <= '9';
    }

    internal static string Opaque(string value, string idName)
    {
        ArgumentNullException.ThrowIfNull(value);

        bool isValid = value.Length is > 0 and <= MaxOpaqueLength
            && value.All(c => c is > ' ' and <= '~');

        return isValid
            ? value
            : throw new ArgumentException(
                $"{idName} must be 1-{MaxOpaqueLength} printable ASCII characters without whitespace.",
                nameof(value));
    }
}
