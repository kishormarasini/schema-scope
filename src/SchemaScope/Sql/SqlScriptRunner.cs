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
    private readonly bool _transactionPerScript;

    public SqlScriptRunner(SqlSession session, RunLogger logger, string databaseName, bool transactionPerScript = false)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        if (string.IsNullOrWhiteSpace(databaseName))
        {
            throw new ArgumentException("Database name is required.", nameof(databaseName));
        }
        _databaseName = databaseName;
        _transactionPerScript = transactionPerScript;
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

        return _transactionPerScript
            ? ExecuteInTransaction(label, batches)
            : ExecuteBatches(label, batches);
    }

    private bool ExecuteBatches(string label, IReadOnlyList<string> batches)
    {
        try
        {
            foreach (var batch in batches)
            {
                _session.ExecuteNonQuery(batch);
            }
            _logger.Success($"OK   {label}");
            return true;
        }
        catch (Exception ex) when (ex is SqlException or InvalidOperationException)
        {
            _logger.Error($"FAIL {label}");
            _logger.Muted(ex.Message);
            return false;
        }
    }

    private bool ExecuteInTransaction(string label, IReadOnlyList<string> batches)
    {
        using var transaction = _session.BeginTransaction();
        try
        {
            foreach (var batch in batches)
            {
                _session.ExecuteNonQuery(batch, transaction);
            }
            transaction.Commit();
            _logger.Success($"OK   {label} (transaction committed)");
            return true;
        }
        catch (Exception ex) when (ex is SqlException or InvalidOperationException)
        {
            try
            {
                transaction.Rollback();
                _logger.Error($"FAIL {label} — rolled back, no partial changes");
            }
            catch (Exception rollbackEx) when (rollbackEx is SqlException or InvalidOperationException)
            {
                _logger.Error($"FAIL {label} — rollback also failed: {rollbackEx.Message}");
            }
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
