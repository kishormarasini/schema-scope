using System.IO;
using System.Linq;
using SchemaScope.Configuration;
using SchemaScope.Sql;
using Xunit;

namespace SchemaScope.Tests;

public class VersionSchemeTests
{
    [Fact]
    public void Default_scheme_matches_legacy_naming()
    {
        var scheme = new VersionScheme();
        Assert.Equal("1.0.0.7", scheme.Label(7));
        Assert.Equal("1.0.0.7.sql", scheme.FileName(7));

        var match = scheme.CompilePattern().Match("1.0.0.42.sql");
        Assert.True(match.Success);
        Assert.Equal("42", match.Groups[1].Value);
    }

    [Fact]
    public void Custom_scheme_is_honoured()
    {
        var scheme = new VersionScheme
        {
            FilePattern = @"^V(\d+)__.*\.sql$",
            FileNameFormat = "V{0}__migration.sql",
            LabelFormat = "V{0}"
        };

        Assert.Equal("V12", scheme.Label(12));
        Assert.Equal("V12__migration.sql", scheme.FileName(12));

        var match = scheme.CompilePattern().Match("V12__add_users.sql");
        Assert.True(match.Success);
        Assert.Equal("12", match.Groups[1].Value);
    }
}

public class VersionScriptLocatorTests
{
    [Fact]
    public void GetInRange_returns_matching_scripts_in_order_within_bounds()
    {
        var dir = CreateTempDir();
        try
        {
            foreach (var n in new[] { 1, 2, 3, 10 })
            {
                File.WriteAllText(Path.Combine(dir, $"1.0.0.{n}.sql"), "SELECT 1;");
            }
            File.WriteAllText(Path.Combine(dir, "not-a-version.sql"), "SELECT 1;");

            var locator = new VersionScriptLocator(dir, new VersionScheme());
            var inRange = locator.GetInRange(2, 10).Select(s => s.Number).ToList();

            Assert.Equal(new[] { 2, 3, 10 }, inRange);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Custom_pattern_locates_non_legacy_files()
    {
        var dir = CreateTempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "V5__seed.sql"), "SELECT 1;");

            var scheme = new VersionScheme
            {
                FilePattern = @"^V(\d+)__.*\.sql$",
                FileNameFormat = "V{0}__seed.sql",
                LabelFormat = "V{0}"
            };
            var locator = new VersionScriptLocator(dir, scheme);

            var single = locator.GetSingle(5);
            Assert.NotNull(single);
            Assert.Equal("V5", single!.Label);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "SchemaScopeTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
