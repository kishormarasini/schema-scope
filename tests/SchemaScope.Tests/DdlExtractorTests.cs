using System.Linq;
using SchemaScope.Parsing;
using Xunit;

namespace SchemaScope.Tests;

public class DdlExtractorTests
{
    private static DdlExtractionResult Extract(string sql) => new DdlExtractor().Extract(sql);

    [Fact]
    public void CreateTable_emits_table_marker_and_per_column_types()
    {
        var result = Extract("CREATE TABLE dbo.Customer (Id INT NOT NULL, Name NVARCHAR(50) NULL);");

        var table = result.Objects.Single(o => o.Kind == DdlObjectKind.Table);
        Assert.Equal("Customer", table.Name);

        var columns = result.Objects.Where(o => o.Kind == DdlObjectKind.Column).ToList();
        Assert.Equal(2, columns.Count);

        var id = columns.Single(c => c.Name == "Id");
        Assert.Equal("int|NOT NULL", id.ColumnType);

        var name = columns.Single(c => c.Name == "Name");
        Assert.Equal("nvarchar(50)|NULL", name.ColumnType);
    }

    [Fact]
    public void Nvarchar_max_is_reported_as_max_not_minus_one()
    {
        var result = Extract("ALTER TABLE dbo.Doc ADD Body NVARCHAR(MAX) NULL;");

        var col = result.Objects.Single(o => o.Kind == DdlObjectKind.Column && o.Name == "Body");
        Assert.Equal("nvarchar(max)|NULL", col.ColumnType);
    }

    [Fact]
    public void Varchar_max_is_reported_as_max()
    {
        var result = Extract("ALTER TABLE dbo.Doc ADD Body VARCHAR(MAX) NOT NULL;");

        var col = result.Objects.Single(o => o.Kind == DdlObjectKind.Column && o.Name == "Body");
        Assert.Equal("varchar(max)|NOT NULL", col.ColumnType);
    }

    [Fact]
    public void Decimal_captures_precision_and_scale()
    {
        var result = Extract("ALTER TABLE dbo.Money ADD Amount DECIMAL(18, 2) NOT NULL;");

        var col = result.Objects.Single(o => o.Kind == DdlObjectKind.Column && o.Name == "Amount");
        Assert.Equal("decimal(18,2)|NOT NULL", col.ColumnType);
    }

    [Fact]
    public void Captures_non_dbo_schema_on_table_and_columns()
    {
        var result = Extract("CREATE TABLE sales.Orders (Id INT NOT NULL);");

        var table = result.Objects.Single(o => o.Kind == DdlObjectKind.Table);
        Assert.Equal("sales", table.Schema);

        var col = result.Objects.Single(o => o.Kind == DdlObjectKind.Column);
        Assert.Equal("sales", col.Schema);
        Assert.Equal("Orders", col.Parent);
    }

    [Fact]
    public void Unqualified_names_leave_schema_null()
    {
        var result = Extract("CREATE TABLE Widget (Id INT NOT NULL);");

        var table = result.Objects.Single(o => o.Kind == DdlObjectKind.Table);
        Assert.Null(table.Schema);
    }

    [Fact]
    public void Captures_inline_and_table_level_named_constraints()
    {
        var sql = @"
CREATE TABLE dbo.Item (
    Id INT NOT NULL CONSTRAINT PK_Item PRIMARY KEY,
    SupplierId INT NOT NULL,
    CONSTRAINT FK_Item_Supplier FOREIGN KEY (SupplierId) REFERENCES dbo.Supplier(Id)
);";
        var result = Extract(sql);

        var constraints = result.Objects
            .Where(o => o.Kind == DdlObjectKind.Constraint)
            .Select(o => o.Name)
            .ToList();

        Assert.Contains("PK_Item", constraints);
        Assert.Contains("FK_Item_Supplier", constraints);
    }

    [Fact]
    public void Captures_standalone_index_with_keys_and_includes()
    {
        var result = Extract("CREATE INDEX IX_Customer_Name ON dbo.Customer (LastName, FirstName) INCLUDE (Email);");

        var index = result.Objects.Single(o => o.Kind == DdlObjectKind.Index);
        Assert.Equal("IX_Customer_Name", index.Name);
        Assert.Equal("Customer", index.Parent);
        Assert.Equal("LastName,FirstName", index.IndexKeys);
        Assert.Equal("Email", index.IndexIncludes);
    }

    [Fact]
    public void Captures_procedures_views_and_functions()
    {
        var sql = @"
CREATE PROCEDURE dbo.GetOne AS SELECT 1;
GO
CREATE OR ALTER VIEW dbo.V AS SELECT 1 AS X;
GO
CREATE FUNCTION dbo.AddOne (@x INT) RETURNS INT AS BEGIN RETURN @x + 1; END;";
        var result = Extract(sql);

        Assert.Contains(result.Objects, o => o.Kind == DdlObjectKind.Procedure && o.Name == "GetOne");
        Assert.Contains(result.Objects, o => o.Kind == DdlObjectKind.View && o.Name == "V");
        Assert.Contains(result.Objects, o => o.Kind == DdlObjectKind.Function && o.Name == "AddOne");
    }

    [Fact]
    public void Reports_parse_errors_for_malformed_sql()
    {
        var result = Extract("CREATE TABLE dbo.Broken (Id INT NOT NULL");
        Assert.NotEmpty(result.ParseErrors);
    }

    [Fact]
    public void Empty_input_yields_no_objects()
    {
        var result = Extract("   ");
        Assert.Empty(result.Objects);
        Assert.Empty(result.ParseErrors);
    }
}
