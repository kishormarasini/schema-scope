using Microsoft.Data.SqlClient;
using SchemaScope.Configuration;

namespace SchemaScope.Sql;

public sealed class SqlConnectionFactory
{
    private readonly ConnectionSettings _settings;

    public string Server { get; }

    public int CommandTimeoutSeconds => _settings.CommandTimeoutSeconds;

    public SqlConnectionFactory(string server, ConnectionSettings settings)
    {
        if (string.IsNullOrWhiteSpace(server))
        {
            throw new ArgumentException("Server is required.", nameof(server));
        }
        Server = server;
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    public string BuildConnectionString(string database)
    {
        if (string.IsNullOrWhiteSpace(database))
        {
            throw new ArgumentException("Database is required.", nameof(database));
        }
        return _settings.ToBuilder(Server, database).ConnectionString;
    }

    public SqlConnection Open(string database)
    {
        var conn = new SqlConnection(BuildConnectionString(database));
        conn.Open();
        return conn;
    }
}
