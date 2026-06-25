using System.Reflection;

namespace SchemaScope;

internal static class AppInfo
{
    public const string Name    = "SchemaScope";
    public const string Tagline = "SQL Server schema runner and verifier";
    public const string Author  = "Kishor Marasini";

    public static string Version { get; } =
        typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(AppInfo).Assembly.GetName().Version?.ToString(3)
        ?? "0.0.0";

    public const string License = "MIT";
}
