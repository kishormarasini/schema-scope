using System.IO;
using SchemaScope.Parsing;
using SchemaScope.Sql;
using Microsoft.Data.SqlClient;

namespace SchemaScope.Verification;

public sealed class VersionVerifier
{
    private readonly SqlSession _session;
    private readonly string _defaultSchema;

    public VersionVerifier(SqlSession session, string defaultSchema = "dbo")
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _defaultSchema = string.IsNullOrWhiteSpace(defaultSchema) ? "dbo" : defaultSchema;
    }

    public VerificationReport Verify(int version, IReadOnlyList<DdlObject> objects, IReadOnlyList<string> parseWarnings)
    {
        var results = new List<VerificationResult>(objects.Count);
        foreach (var obj in objects)
        {
            results.Add(obj.Kind switch
            {
                DdlObjectKind.Table      => VerifyTable(obj),
                DdlObjectKind.Column     => VerifyColumn(obj),
                DdlObjectKind.Index      => VerifyIndex(obj),
                DdlObjectKind.Constraint => VerifyConstraint(obj),
                DdlObjectKind.Procedure  => VerifyModule(obj, version, typeFilter: "'P','PC'"),
                DdlObjectKind.View       => VerifyModule(obj, version, typeFilter: "'V'"),
                DdlObjectKind.Trigger    => VerifyModule(obj, version, typeFilter: "'TR','TA'"),
                DdlObjectKind.Function   => VerifyModule(obj, version, typeFilter: "'FN','IF','TF','AF','FS','FT'"),
                _ => Missing(obj, "unsupported object kind")
            });
        }

        return new VerificationReport
        {
            Version = version,
            Results = results,
            ParseWarnings = parseWarnings
        };
    }

    private string SchemaOf(DdlObject obj) => obj.Schema ?? _defaultSchema;

    private string Qualified(DdlObject obj, string name) => $"{SchemaOf(obj)}.{name}";

    private VerificationResult VerifyTable(DdlObject obj)
    {
        return ExistsAnyObject(SchemaOf(obj), obj.Name, "'U'")
            ? Matches(obj, "table present")
            : Missing(obj, "table not present");
    }

    private VerificationResult VerifyColumn(DdlObject obj)
    {
        if (string.IsNullOrWhiteSpace(obj.Parent))
        {
            return Missing(obj, "no parent table recorded");
        }

        var parent = Qualified(obj, obj.Parent);

        var expected = obj.ColumnType ?? string.Empty;
        var actual = GetColumnSpec(parent, obj.Name);

        if (string.IsNullOrEmpty(actual))
        {
            return Missing(obj, "column not present");
        }

        var expectedNorm = SqlNormalizer.NormalizeColumnSpec(expected);
        var actualNorm   = SqlNormalizer.NormalizeColumnSpec(actual);

        if (expectedNorm == actualNorm)
        {
            return Matches(obj, $"type matches ({actual})");
        }
        return Differs(obj, $"type differs (db: {actual}  expected: {expected})");
    }

    private VerificationResult VerifyIndex(DdlObject obj)
    {
        if (string.IsNullOrWhiteSpace(obj.Parent))
        {
            return Missing(obj, "no parent table recorded");
        }

        var parent = Qualified(obj, obj.Parent);

        var (exists, keys, includes) = GetIndexColumns(parent, obj.Name);
        if (!exists)
        {
            return Missing(obj, "index not present");
        }

        var actualSpec = $"keys={keys};includes={includes}";
        var expectedSpec = $"keys={obj.IndexKeys};includes={obj.IndexIncludes}";

        if (SqlNormalizer.NormalizeColumnSpec(actualSpec) == SqlNormalizer.NormalizeColumnSpec(expectedSpec))
        {
            return Matches(obj, $"columns match ({actualSpec})");
        }

        return Differs(obj, $"columns differ (db: {actualSpec}  expected: {expectedSpec})");
    }

    private VerificationResult VerifyConstraint(DdlObject obj)
    {
        return ExistsAnyObject(SchemaOf(obj), obj.Name, "'C','D','F','PK','UQ','CK'")
            ? Matches(obj, "constraint present")
            : Missing(obj, "constraint not present");
    }

    private VerificationResult VerifyModule(DdlObject obj, int version, string typeFilter)
    {
        if (!ExistsAnyObject(SchemaOf(obj), obj.Name, typeFilter))
        {
            return Missing(obj, "module not present");
        }

        var dbDefinition = GetModuleDefinition(Qualified(obj, obj.Name));
        var comparison = ModuleBodyComparer.Compare(obj.DefinitionText, dbDefinition);

        if (comparison.AreEquivalent)
        {
            return Matches(obj, "body matches");
        }

        Directory.CreateDirectory(AppPaths.DiffsDir);
        var dbPath         = Path.Combine(AppPaths.DiffsDir, $"{obj.Name}_{version}_db.sql");
        var filePath       = Path.Combine(AppPaths.DiffsDir, $"{obj.Name}_{version}_file.sql");
        var dbCanonPath    = Path.Combine(AppPaths.DiffsDir, $"{obj.Name}_{version}_db.canonical.sql");
        var fileCanonPath  = Path.Combine(AppPaths.DiffsDir, $"{obj.Name}_{version}_file.canonical.sql");

        try
        {
            File.WriteAllText(dbPath,        dbDefinition);
            File.WriteAllText(filePath,      obj.DefinitionText);
            File.WriteAllText(dbCanonPath,   comparison.DbCanonical);
            File.WriteAllText(fileCanonPath, comparison.FileCanonical);
        }
        catch (IOException)
        {
            return Differs(obj, "body differs (diff dump failed)");
        }

        var detail = comparison.FileParseErrors.Count > 0 || comparison.DbParseErrors.Count > 0
            ? "body differs (parse warnings present)"
            : "body differs";

        return new VerificationResult
        {
            Object = obj,
            Status = VerificationStatus.Differs,
            Detail = detail,
            DbDumpPath = dbPath,
            FileDumpPath = filePath
        };
    }

    private bool ExistsAnyObject(string schema, string name, string typeFilter)
    {
        using var cmd = _session.CreateCommand(
            $@"SELECT CASE WHEN EXISTS (
                   SELECT 1
                   FROM sys.objects o
                   JOIN sys.schemas s ON o.schema_id = s.schema_id
                   WHERE o.name = @name AND s.name = @schema AND o.type IN ({typeFilter})
               ) THEN 1 ELSE 0 END;");
        cmd.Parameters.Add(new SqlParameter("@name", name));
        cmd.Parameters.Add(new SqlParameter("@schema", schema));
        var scalar = cmd.ExecuteScalar();
        return scalar is not null && scalar != DBNull.Value && Convert.ToInt32(scalar) == 1;
    }

    private string GetColumnSpec(string qualifiedTable, string column)
    {
        using var cmd = _session.CreateCommand(@"
SELECT TOP 1
  LOWER(t.name) +
  CASE
    WHEN c.max_length = -1                                    THEN '(max)'
    WHEN t.name IN ('varchar','char','varbinary','binary')   THEN '(' + CAST(c.max_length   AS VARCHAR) + ')'
    WHEN t.name IN ('nvarchar','nchar')                      THEN '(' + CAST(c.max_length/2 AS VARCHAR) + ')'
    WHEN t.name IN ('decimal','numeric')                     THEN '(' + CAST(c.precision    AS VARCHAR) + ',' + CAST(c.scale AS VARCHAR) + ')'
    ELSE ''
  END
  + '|' + CASE WHEN c.is_nullable = 1 THEN 'NULL' ELSE 'NOT NULL' END
FROM sys.columns c
JOIN sys.types t ON c.user_type_id = t.user_type_id
WHERE c.object_id = OBJECT_ID(@table) AND c.name = @column;");
        cmd.Parameters.Add(new SqlParameter("@table", qualifiedTable));
        cmd.Parameters.Add(new SqlParameter("@column", column));
        var result = cmd.ExecuteScalar();
        if (result is null || result == DBNull.Value)
        {
            return string.Empty;
        }
        return result.ToString()?.Trim() ?? string.Empty;
    }

    private (bool Exists, string Keys, string Includes) GetIndexColumns(string qualifiedTable, string indexName)
    {
        using var cmd = _session.CreateCommand(@"
DECLARE @objId INT = OBJECT_ID(@table);
DECLARE @indexId INT = INDEXPROPERTY(@objId, @idx, 'IndexID');
DECLARE @k NVARCHAR(MAX) = '', @i NVARCHAR(MAX) = '';

SELECT @k = STUFF((
    SELECT ',' + c.name
    FROM sys.index_columns ic
    JOIN sys.columns c ON ic.object_id = c.object_id AND ic.column_id = c.column_id
    WHERE ic.object_id = @objId AND ic.index_id = @indexId AND ic.is_included_column = 0
    ORDER BY ic.key_ordinal
    FOR XML PATH('')), 1, 1, '');

SELECT @i = STUFF((
    SELECT ',' + c.name
    FROM sys.index_columns ic
    JOIN sys.columns c ON ic.object_id = c.object_id AND ic.column_id = c.column_id
    WHERE ic.object_id = @objId AND ic.index_id = @indexId AND ic.is_included_column = 1
    ORDER BY c.name
    FOR XML PATH('')), 1, 1, '');

SELECT CASE WHEN @indexId IS NOT NULL THEN 1 ELSE 0 END, ISNULL(@k, ''), ISNULL(@i, '');");
        cmd.Parameters.Add(new SqlParameter("@table", qualifiedTable));
        cmd.Parameters.Add(new SqlParameter("@idx", indexName));

        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
        {
            return (false, string.Empty, string.Empty);
        }

        var exists = !reader.IsDBNull(0) && reader.GetInt32(0) == 1;
        var keys = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
        var includes = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
        return (exists, keys, includes);
    }

    private string GetModuleDefinition(string qualifiedName)
    {
        using var cmd = _session.CreateCommand(
            "SELECT ISNULL(OBJECT_DEFINITION(OBJECT_ID(@name)), '')");
        cmd.Parameters.Add(new SqlParameter("@name", qualifiedName));
        var result = cmd.ExecuteScalar();
        if (result is null || result == DBNull.Value)
        {
            return string.Empty;
        }
        return result.ToString() ?? string.Empty;
    }

    private static VerificationResult Matches(DdlObject obj, string detail) =>
        new() { Object = obj, Status = VerificationStatus.Matches, Detail = detail };

    private static VerificationResult Differs(DdlObject obj, string detail) =>
        new() { Object = obj, Status = VerificationStatus.Differs, Detail = detail };

    private static VerificationResult Missing(DdlObject obj, string detail) =>
        new() { Object = obj, Status = VerificationStatus.Missing, Detail = detail };
}
