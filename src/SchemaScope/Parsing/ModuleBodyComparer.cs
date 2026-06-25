using System.IO;
using System.Text.RegularExpressions;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SchemaScope.Parsing;

public static class ModuleBodyComparer
{
    // OBJECT_DEFINITION always returns the module as CREATE, regardless of whether the
    // original DDL was CREATE, ALTER, or CREATE OR ALTER. SQL Server blanks "OR ALTER"
    // to whitespace in sys.sql_modules. We fold all three forms to CREATE before comparing.
    private static readonly Regex CreateOrAlterFold = new(
        @"^\s*CREATE\s+OR\s+ALTER\s+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex AlterModuleFold = new(
        @"^\s*ALTER\s+(PROCEDURE|PROC|VIEW|TRIGGER|FUNCTION)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public sealed class ComparisonOutcome
    {
        public required bool AreEquivalent { get; init; }
        public required string FileCanonical { get; init; }
        public required string DbCanonical { get; init; }
        public IReadOnlyList<string> FileParseErrors { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> DbParseErrors { get; init; } = Array.Empty<string>();
    }

    public static ComparisonOutcome Compare(string fileDefinition, string dbDefinition)
    {
        var (fileCanonical, fileErrors) = Canonicalize(fileDefinition);
        var (dbCanonical, dbErrors) = Canonicalize(dbDefinition);

        return new ComparisonOutcome
        {
            AreEquivalent = !string.IsNullOrEmpty(fileCanonical)
                            && string.Equals(fileCanonical, dbCanonical, StringComparison.Ordinal),
            FileCanonical = fileCanonical,
            DbCanonical = dbCanonical,
            FileParseErrors = fileErrors,
            DbParseErrors = dbErrors
        };
    }

    private static (string Canonical, IReadOnlyList<string> Errors) Canonicalize(string? sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            return (string.Empty, Array.Empty<string>());
        }

        var parser = new TSql160Parser(initialQuotedIdentifiers: true);

        TSqlFragment? fragment;
        IList<ParseError> parseErrors;
        using (var reader = new StringReader(sql))
        {
            fragment = parser.Parse(reader, out parseErrors);
        }

        var errors = parseErrors
            .Select(e => $"Line {e.Line}, Col {e.Column}: {e.Message}")
            .ToList();

        if (fragment is null)
        {
            return (string.Empty, errors);
        }

        var generator = new Sql160ScriptGenerator(BuildOptions());
        generator.GenerateScript(fragment, out var canonical);
        canonical ??= string.Empty;

        canonical = CreateOrAlterFold.Replace(canonical, "CREATE ");
        canonical = AlterModuleFold.Replace(canonical, "CREATE $1");

        return (canonical.Trim(), errors);
    }

    private static SqlScriptGeneratorOptions BuildOptions() => new()
    {
        KeywordCasing = KeywordCasing.Uppercase,
        IncludeSemicolons = true,
        SqlVersion = SqlVersion.Sql160,
        AlignClauseBodies = false,
        AlignColumnDefinitionFields = false,
        AlignSetClauseItem = false,
        AsKeywordOnOwnLine = false,
        IndentationSize = 2,
        IndentSetClause = false,
        IndentViewBody = false,
        MultilineInsertSourcesList = false,
        MultilineInsertTargetsList = false,
        MultilineSelectElementsList = false,
        MultilineSetClauseItems = false,
        MultilineViewColumnsList = false,
        MultilineWherePredicatesList = false,
        NewLineBeforeCloseParenthesisInMultilineList = false,
        NewLineBeforeFromClause = false,
        NewLineBeforeGroupByClause = false,
        NewLineBeforeHavingClause = false,
        NewLineBeforeJoinClause = false,
        NewLineBeforeOpenParenthesisInMultilineList = false,
        NewLineBeforeOrderByClause = false,
        NewLineBeforeOutputClause = false,
        NewLineBeforeWhereClause = false
    };
}
