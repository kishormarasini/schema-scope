using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using SchemaScope.Ui;
using Spectre.Console;

namespace SchemaScope.Sql;

public sealed class DatabaseOperations
{
    private static readonly Regex SafeIdentifier = new(@"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);

    private readonly SqlConnectionFactory _factory;
    private readonly RunLogger _logger;

    public DatabaseOperations(SqlConnectionFactory factory, RunLogger logger)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public bool Backup(string sourceDb, string backupPath, Action<int>? onProgress = null, Action<string>? onSummary = null)
    {
        if (!ValidateIdentifier(sourceDb, "Source database")) return false;
        if (string.IsNullOrWhiteSpace(backupPath))
        {
            _logger.Error("Backup path is required.");
            return false;
        }

        try
        {
            var dir = Path.GetDirectoryName(backupPath);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }
        }
        catch (IOException ex)
        {
            _logger.Error($"Cannot create backup directory: {ex.Message}");
            return false;
        }

        _logger.LogOnly($"Backup {sourceDb} -> {backupPath}");

        try
        {
            using var conn = OpenMaster();
            HookProgress(conn, onProgress, onSummary);
            using var cmd = conn.CreateCommand();
            cmd.CommandTimeout = 0;
            cmd.CommandText =
                $"BACKUP DATABASE [{sourceDb}] TO DISK = @path WITH FORMAT, INIT, NAME = @name, STATS = 10;";
            cmd.Parameters.Add(new SqlParameter("@path", backupPath));
            cmd.Parameters.Add(new SqlParameter("@name", $"Full backup of {sourceDb}"));
            cmd.ExecuteNonQuery();

            _logger.LogOnly($"OK Backup complete: {backupPath}");
            return true;
        }
        catch (SqlException ex)
        {
            _logger.Error("FAIL Backup");
            _logger.Muted(ex.Message);
            return false;
        }
    }

    public bool Restore(string targetDb, string backupPath, string? dataDir = null, string? logDir = null,
        Action<int>? onProgress = null, Action<string>? onSummary = null)
    {
        if (!ValidateIdentifier(targetDb, "Target database")) return false;
        if (string.IsNullOrWhiteSpace(backupPath) || !File.Exists(backupPath))
        {
            _logger.Error($"Backup file not found: {backupPath}");
            return false;
        }

        _logger.LogOnly($"Restore {targetDb} <- {backupPath}");

        try
        {
            using var conn = OpenMaster();
            HookProgress(conn, onProgress, onSummary);

            var (dataLogical, logLogical) = GetLogicalFileNames(conn, backupPath);
            if (dataLogical is null || logLogical is null)
            {
                _logger.Error("Could not determine logical file names from backup.");
                return false;
            }

            dataDir = string.IsNullOrWhiteSpace(dataDir) ? GetServerDefaultPath(conn, "InstanceDefaultDataPath") : dataDir;
            logDir  = string.IsNullOrWhiteSpace(logDir)  ? GetServerDefaultPath(conn, "InstanceDefaultLogPath")  : logDir;

            if (string.IsNullOrWhiteSpace(dataDir) || string.IsNullOrWhiteSpace(logDir))
            {
                _logger.Error("Could not resolve default data/log directories. Supply them explicitly.");
                return false;
            }

            var dataFile = Path.Combine(dataDir, $"{targetDb}.mdf");
            var logFile  = Path.Combine(logDir,  $"{targetDb}_log.ldf");

            onSummary?.Invoke($"data file: {dataFile}");
            onSummary?.Invoke($"log file:  {logFile}");
            _logger.LogOnly($"  data file: {dataFile}");
            _logger.LogOnly($"  log file:  {logFile}");

            if (TargetExists(conn, targetDb))
            {
                onSummary?.Invoke("target exists - switching to SINGLE_USER WITH ROLLBACK IMMEDIATE");
                _logger.LogOnly("  target exists - switching to SINGLE_USER WITH ROLLBACK IMMEDIATE");
                using var alter = conn.CreateCommand();
                alter.CommandTimeout = 120;
                alter.CommandText = $"ALTER DATABASE [{targetDb}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;";
                alter.ExecuteNonQuery();
            }

            using var restore = conn.CreateCommand();
            restore.CommandTimeout = 0;
            restore.CommandText =
                $@"RESTORE DATABASE [{targetDb}]
                  FROM DISK = @path
                  WITH
                      MOVE @dataLogical TO @dataFile,
                      MOVE @logLogical  TO @logFile,
                      REPLACE,
                      STATS = 10;";
            restore.Parameters.Add(new SqlParameter("@path", backupPath));
            restore.Parameters.Add(new SqlParameter("@dataLogical", dataLogical));
            restore.Parameters.Add(new SqlParameter("@dataFile", dataFile));
            restore.Parameters.Add(new SqlParameter("@logLogical", logLogical));
            restore.Parameters.Add(new SqlParameter("@logFile", logFile));
            restore.ExecuteNonQuery();

            using var multi = conn.CreateCommand();
            multi.CommandTimeout = 120;
            multi.CommandText = $"ALTER DATABASE [{targetDb}] SET MULTI_USER;";
            multi.ExecuteNonQuery();

            _logger.LogOnly($"OK Restore complete: {targetDb}");
            return true;
        }
        catch (SqlException ex)
        {
            _logger.Error("FAIL Restore");
            _logger.Muted(ex.Message);
            return false;
        }
    }

    public bool Clone(string sourceDb, string targetDb, string? backupPath = null, string? dataDir = null, string? logDir = null,
        Action<int>? onBackupProgress = null, Action<int>? onRestoreProgress = null, Action<string>? onSummary = null)
    {
        if (!ValidateIdentifier(sourceDb, "Source database")) return false;
        if (!ValidateIdentifier(targetDb, "Target database")) return false;
        if (sourceDb.Equals(targetDb, StringComparison.OrdinalIgnoreCase))
        {
            _logger.Error("Source and target database names must differ.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(backupPath))
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "SchemaScope");
            Directory.CreateDirectory(tempDir);
            backupPath = Path.Combine(tempDir, $"{sourceDb}_{DateTime.Now:yyyyMMdd_HHmmss}.bak");
        }

        _logger.LogOnly($"Clone {sourceDb} -> {targetDb}");

        if (!Backup(sourceDb, backupPath, onBackupProgress, onSummary))
        {
            return false;
        }

        if (!Restore(targetDb, backupPath, dataDir, logDir, onRestoreProgress, onSummary))
        {
            return false;
        }

        _logger.LogOnly($"OK Clone complete: {sourceDb} -> {targetDb}");
        return true;
    }

    public string? TryGetServerDefaultBackupPath()
    {
        try
        {
            using var conn = OpenMaster();
            return GetServerDefaultPath(conn, "InstanceDefaultBackupPath");
        }
        catch
        {
            return null;
        }
    }

    public string? TryGetBackupSourceDatabase(string backupPath)
    {
        if (string.IsNullOrWhiteSpace(backupPath) || !File.Exists(backupPath))
        {
            return null;
        }

        try
        {
            using var conn = OpenMaster();
            using var cmd = conn.CreateCommand();
            cmd.CommandTimeout = 120;
            cmd.CommandText = "RESTORE HEADERONLY FROM DISK = @path;";
            cmd.Parameters.Add(new SqlParameter("@path", backupPath));
            using var reader = cmd.ExecuteReader();
            if (!reader.Read())
            {
                return null;
            }
            var value = reader["DatabaseName"]?.ToString();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
        catch
        {
            return null;
        }
    }

    private SqlConnection OpenMaster() => _factory.Open("master");

    private static readonly Regex PercentPattern = new(@"^\s*(\d+)\s+percent\s+processed",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private void HookProgress(SqlConnection conn, Action<int>? onProgress, Action<string>? onSummary)
    {
        conn.InfoMessage += (_, e) =>
        {
            foreach (SqlError err in e.Errors)
            {
                if (string.IsNullOrWhiteSpace(err.Message))
                {
                    continue;
                }

                var pct = PercentPattern.Match(err.Message);
                if (pct.Success && int.TryParse(pct.Groups[1].Value, out var value))
                {
                    onProgress?.Invoke(value);
                    _logger.LogOnly($"  {err.Message}");
                    continue;
                }

                onSummary?.Invoke(err.Message);
                _logger.LogOnly($"  {err.Message}");
            }
        };
    }

    private static (string? Data, string? Log) GetLogicalFileNames(SqlConnection conn, string backupPath)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandTimeout = 120;
        cmd.CommandText = "RESTORE FILELISTONLY FROM DISK = @path;";
        cmd.Parameters.Add(new SqlParameter("@path", backupPath));

        string? data = null;
        string? log = null;
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var logical = reader["LogicalName"]?.ToString();
            var type    = reader["Type"]?.ToString();
            if (string.IsNullOrWhiteSpace(logical) || string.IsNullOrWhiteSpace(type))
            {
                continue;
            }
            if (type.Equals("D", StringComparison.OrdinalIgnoreCase) && data is null)
            {
                data = logical;
            }
            else if (type.Equals("L", StringComparison.OrdinalIgnoreCase) && log is null)
            {
                log = logical;
            }
        }
        return (data, log);
    }

    private static string? GetServerDefaultPath(SqlConnection conn, string property)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandTimeout = 30;
        cmd.CommandText = $"SELECT CAST(SERVERPROPERTY('{property}') AS NVARCHAR(4000));";
        var result = cmd.ExecuteScalar();
        if (result is null || result == DBNull.Value)
        {
            return null;
        }
        var value = result.ToString();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static bool TargetExists(SqlConnection conn, string targetDb)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandTimeout = 30;
        cmd.CommandText = "SELECT CASE WHEN DB_ID(@db) IS NOT NULL THEN 1 ELSE 0 END;";
        cmd.Parameters.Add(new SqlParameter("@db", targetDb));
        var result = cmd.ExecuteScalar();
        return result is not null && result != DBNull.Value && Convert.ToInt32(result) == 1;
    }

    private bool ValidateIdentifier(string value, string label)
    {
        if (!SafeIdentifier.IsMatch(value))
        {
            _logger.Error($"{label} contains unsafe characters. Use letters, digits, and underscore only.");
            return false;
        }
        return true;
    }
}
