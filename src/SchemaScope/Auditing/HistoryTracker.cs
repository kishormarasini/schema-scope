using Microsoft.Data.SqlClient;
using SchemaScope.Sql;
using SchemaScope.Ui;

namespace SchemaScope.Auditing;

public static class HistoryTracker
{
    public const string Table = "dbo.schemascope_history";

    private const string EnsureTableSql = """
        IF OBJECT_ID('dbo.schemascope_history', 'U') IS NULL
        CREATE TABLE dbo.schemascope_history (
            Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_schemascope_history PRIMARY KEY,
            Version INT NOT NULL,
            ScriptName NVARCHAR(400) NOT NULL,
            AppliedAtUtc DATETIME2 NOT NULL CONSTRAINT DF_schemascope_history_AppliedAtUtc DEFAULT SYSUTCDATETIME(),
            DurationMs INT NULL,
            Success BIT NOT NULL,
            ToolVersion NVARCHAR(50) NULL
        );
        """;

    public static void Record(SqlSession session, RunLogger logger, int version, string scriptName, bool success, long durationMs)
    {
        try
        {
            session.ExecuteNonQuery(EnsureTableSql);

            using var cmd = session.CreateCommand(
                "INSERT INTO dbo.schemascope_history (Version, ScriptName, DurationMs, Success, ToolVersion) " +
                "VALUES (@version, @scriptName, @durationMs, @success, @toolVersion)");
            cmd.Parameters.Add(new SqlParameter("@version", version));
            cmd.Parameters.Add(new SqlParameter("@scriptName", scriptName));
            cmd.Parameters.Add(new SqlParameter("@durationMs", durationMs));
            cmd.Parameters.Add(new SqlParameter("@success", success));
            cmd.Parameters.Add(new SqlParameter("@toolVersion", AppInfo.Version));
            cmd.ExecuteNonQuery();
        }
        catch (Exception ex) when (ex is SqlException or InvalidOperationException)
        {
            logger.Warn($"Could not record history for {scriptName}: {ex.Message}");
        }
    }
}
