using System.Text.RegularExpressions;

namespace SchemaScope.Parsing;

internal static class SqlNormalizer
{
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

    public static string NormalizeColumnSpec(string? spec)
    {
        if (string.IsNullOrWhiteSpace(spec))
        {
            return string.Empty;
        }
        return Whitespace.Replace(spec, string.Empty).ToLowerInvariant();
    }
}
