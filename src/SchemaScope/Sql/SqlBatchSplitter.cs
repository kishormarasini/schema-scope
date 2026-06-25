using System.Text.RegularExpressions;

namespace SchemaScope.Sql;

// GO is not a T-SQL keyword; it's a client-side batch separator. SqlCommand can't
// execute multi-batch scripts, so we split on GO first.
internal static class SqlBatchSplitter
{
    private static readonly Regex GoSeparator = new(
        @"^\s*GO(?:\s+\d+)?\s*(?:--[^\r\n]*)?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);

    public static IReadOnlyList<string> Split(string script)
    {
        if (string.IsNullOrWhiteSpace(script))
        {
            return Array.Empty<string>();
        }

        var batches = new List<string>();
        var lastIndex = 0;

        foreach (Match match in GoSeparator.Matches(script))
        {
            var length = match.Index - lastIndex;
            if (length > 0)
            {
                var batch = script.Substring(lastIndex, length).Trim();
                if (!string.IsNullOrWhiteSpace(batch))
                {
                    batches.Add(batch);
                }
            }
            lastIndex = match.Index + match.Length;
        }

        if (lastIndex < script.Length)
        {
            var tail = script[lastIndex..].Trim();
            if (!string.IsNullOrWhiteSpace(tail))
            {
                batches.Add(tail);
            }
        }

        return batches;
    }
}
