using System.IO;

namespace SchemaScope;

internal static class AppPaths
{
    public static string RootDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SchemaScope");

    public static string ConfigFile { get; } = Path.Combine(RootDir, "config.json");

    public static string SeedConfigFile { get; } = Path.Combine(AppContext.BaseDirectory, "config.json");

    public static string LogsDir { get; } = Path.Combine(RootDir, "logs");

    public static string DiffsDir { get; } = Path.Combine(LogsDir, "diffs");

    public static string TestDbsRegistryFile { get; } = Path.Combine(RootDir, "testdbs.json");

    public static string TestDbScriptsDir { get; } = Path.Combine(AppContext.BaseDirectory, "TestDbScripts");

    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(RootDir);
        Directory.CreateDirectory(LogsDir);
    }
}
