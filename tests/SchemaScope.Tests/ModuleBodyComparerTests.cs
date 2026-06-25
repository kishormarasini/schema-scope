using SchemaScope.Parsing;
using Xunit;

namespace SchemaScope.Tests;

public class ModuleBodyComparerTests
{
    [Fact]
    public void Create_and_create_or_alter_are_equivalent()
    {
        const string file = "CREATE OR ALTER PROCEDURE dbo.P AS SELECT 1;";
        const string db   = "CREATE PROCEDURE dbo.P AS SELECT 1;";

        Assert.True(ModuleBodyComparer.Compare(file, db).AreEquivalent);
    }

    [Fact]
    public void Alter_folds_to_create()
    {
        const string file = "ALTER PROCEDURE dbo.P AS SELECT 1;";
        const string db   = "CREATE PROCEDURE dbo.P AS SELECT 1;";

        Assert.True(ModuleBodyComparer.Compare(file, db).AreEquivalent);
    }

    [Fact]
    public void Whitespace_and_keyword_case_differences_are_ignored()
    {
        const string file = "create   procedure   dbo.P\nas\n    select 1;";
        const string db   = "CREATE PROCEDURE dbo.P AS SELECT 1;";

        Assert.True(ModuleBodyComparer.Compare(file, db).AreEquivalent);
    }

    [Fact]
    public void Bracketed_identifiers_match_their_bracketed_db_form()
    {
        const string file = "CREATE OR ALTER PROCEDURE [dbo].[P] AS SELECT 1;";
        const string db   = "CREATE PROCEDURE [dbo].[P] AS SELECT 1;";

        Assert.True(ModuleBodyComparer.Compare(file, db).AreEquivalent);
    }

    [Fact]
    public void Different_bodies_are_not_equivalent()
    {
        const string file = "CREATE PROCEDURE dbo.P AS SELECT 1;";
        const string db   = "CREATE PROCEDURE dbo.P AS SELECT 2;";

        Assert.False(ModuleBodyComparer.Compare(file, db).AreEquivalent);
    }

    [Fact]
    public void Empty_file_definition_is_not_equivalent_to_a_real_body()
    {
        var outcome = ModuleBodyComparer.Compare(string.Empty, "CREATE PROCEDURE dbo.P AS SELECT 1;");
        Assert.False(outcome.AreEquivalent);
    }

    [Fact]
    public void Unparseable_body_surfaces_parse_errors()
    {
        var outcome = ModuleBodyComparer.Compare("CREATE PROCEDURE dbo.P AS SELECT", "CREATE PROCEDURE dbo.P AS SELECT 1;");
        Assert.NotEmpty(outcome.FileParseErrors);
    }
}
