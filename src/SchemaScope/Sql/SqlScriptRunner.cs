using System.IO;
using Microsoft.Data.SqlClient;
using SchemaScope.Ui;
using Spectre.Console;

namespace SchemaScope.Sql;

public sealed class SqlScriptRunner
{
    private readonly SqlSession _session;
    private readonly RunLogger _logger;
    private readonly string _databaseName;

    public SqlScriptRunner(SqlSession session, RunLogger logger, string databaseName)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        if (string.IsNullOrWhiteSpace(databaseName))
        {
            throw new ArgumentException("Database name is required.", nameof(databaseName));
        }
        _databaseName = databaseName;
    }

    public bool Execute(string label, string filePath)
    {
        _logger.Blank();
        _logger.Markup($"[bold {Theme.Info}]{Markup.Escape(label)}[/]");

        if (!File.Exists(filePath))
        {
            _logger.Error($"FAIL {label} : file not found at {filePath}");
            return false;
        }

        string rawSql;
        try
        {
            rawSql = File.ReadAllText(filePath);
        }
        catch (IOException ex)
        {
            _logger.Error($"FAIL {label} : could not read file ({ex.Message})");
            return false;
        }

        var patched = ApplySubstitutions(rawSql, _databaseName);
        var batches = SqlBatchSplitter.Split(patched);

        if (batches.Count == 0)
        {
            _logger.Warn($"SKIP {label} : file has no executable content.");
            return true;
        }

        try
        {
            foreach (var batch in batches)
            {
                _session.ExecuteNonQuery(batch);
            }
            _logger.Success($"OK   {label}");
            return true;
        }
        catch (SqlException ex)
        {
            _logger.Error($"FAIL {label}");
            _logger.Muted(ex.Message);
            return false;
        }
        catch (InvalidOperationException ex)
        {
            _logger.Error($"FAIL {label}");
            _logger.Muted(ex.Message);
            return false;
        }
    }

    internal static string ApplySubstitutions(string sql, string databaseName)
    {
        if (string.IsNullOrEmpty(sql))
        {
            return string.Empty;
        }

        return sql.Replace("[DatabaseName]", databaseName, StringComparison.Ordinal);
    }
}
