using Microsoft.Data.SqlClient;

namespace SchemaScope.Configuration;

public enum AuthenticationMode
{
    Windows,
    SqlPassword
}

public sealed class ConnectionSettings
{
    public const string PasswordEnvVar = "SCHEMASCOPE_PASSWORD";

    public AuthenticationMode Authentication { get; set; } = AuthenticationMode.Windows;
    public string UserId { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public bool Encrypt { get; set; }
    public bool TrustServerCertificate { get; set; } = true;
    public int ConnectTimeoutSeconds { get; set; } = 15;
    public int CommandTimeoutSeconds { get; set; } = 600;

    public SqlConnectionStringBuilder ToBuilder(string server, string database)
    {
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = server,
            InitialCatalog = database,
            Encrypt = Encrypt,
            TrustServerCertificate = TrustServerCertificate,
            ConnectTimeout = ConnectTimeoutSeconds,
            ApplicationName = "SchemaScope"
        };

        if (Authentication == AuthenticationMode.SqlPassword)
        {
            builder.IntegratedSecurity = false;
            builder.UserID = UserId;
            builder.Password = EffectivePassword;
        }
        else
        {
            builder.IntegratedSecurity = true;
        }

        return builder;
    }

    private string EffectivePassword =>
        Environment.GetEnvironmentVariable(PasswordEnvVar) is { Length: > 0 } fromEnv ? fromEnv : Password;
}
