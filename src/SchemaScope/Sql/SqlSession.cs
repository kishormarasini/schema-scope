using System.Data;
using Microsoft.Data.SqlClient;

namespace SchemaScope.Sql;

public sealed class SqlSession : IDisposable
{
    private readonly SqlConnection _connection;
    private readonly int _commandTimeoutSeconds;

    public string Server { get; }
    public string Database { get; }

    private SqlSession(string server, string database, SqlConnection connection, int commandTimeoutSeconds)
    {
        Server = server;
        Database = database;
        _connection = connection;
        _commandTimeoutSeconds = commandTimeoutSeconds;
    }

    public static SqlSession Open(SqlConnectionFactory factory, string database)
    {
        ArgumentNullException.ThrowIfNull(factory);
        if (string.IsNullOrWhiteSpace(database))
        {
            throw new ArgumentException("Database is required.", nameof(database));
        }

        var conn = factory.Open(database);
        return new SqlSession(factory.Server, database, conn, factory.CommandTimeoutSeconds);
    }

    public static bool TryTestConnection(SqlConnectionFactory factory, string database, out string? error)
    {
        try
        {
            using var session = Open(factory, database);
            using var cmd = session.CreateCommand("SELECT 1");
            cmd.ExecuteScalar();
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public SqlCommand CreateCommand(string commandText)
    {
        var cmd = _connection.CreateCommand();
        cmd.CommandText = commandText;
        cmd.CommandTimeout = _commandTimeoutSeconds;
        return cmd;
    }

    public void ExecuteNonQuery(string sql)
    {
        using var cmd = CreateCommand(sql);
        cmd.ExecuteNonQuery();
    }

    public void Dispose()
    {
        if (_connection.State != ConnectionState.Closed)
        {
            _connection.Close();
        }
        _connection.Dispose();
    }
}
