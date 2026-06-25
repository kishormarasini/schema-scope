using System.Linq;
using SchemaScope.Parsing;
using SchemaScope.Sql;
using Xunit;

namespace SchemaScope.Tests;

public class SqlBatchSplitterTests
{
    [Fact]
    public void Splits_on_go_separator()
    {
        var batches = SqlBatchSplitter.Split("SELECT 1;\nGO\nSELECT 2;");
        Assert.Equal(2, batches.Count);
        Assert.Equal("SELECT 1;", batches[0]);
        Assert.Equal("SELECT 2;", batches[1]);
    }

    [Fact]
    public void Go_is_case_insensitive_and_tolerates_trailing_count()
    {
        var batches = SqlBatchSplitter.Split("SELECT 1;\ngo 3\nSELECT 2;");
        Assert.Equal(2, batches.Count);
    }

    [Fact]
    public void Does_not_split_on_go_inside_an_identifier()
    {
        var batches = SqlBatchSplitter.Split("SELECT GoalCount FROM dbo.Targets;");
        Assert.Single(batches);
    }

    [Fact]
    public void Empty_script_yields_no_batches()
    {
        Assert.Empty(SqlBatchSplitter.Split("   "));
    }
}

public class SqlNormalizerTests
{
    [Fact]
    public void Strips_whitespace_and_lowercases()
    {
        Assert.Equal("nvarchar(50)|null", SqlNormalizer.NormalizeColumnSpec("NVARCHAR(50) | NULL"));
    }

    [Fact]
    public void Null_or_empty_normalizes_to_empty()
    {
        Assert.Equal(string.Empty, SqlNormalizer.NormalizeColumnSpec(null));
        Assert.Equal(string.Empty, SqlNormalizer.NormalizeColumnSpec("   "));
    }
}
