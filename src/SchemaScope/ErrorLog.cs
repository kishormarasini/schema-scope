using System.IO;

namespace SchemaScope;

internal static class ErrorLog
{
    private static readonly object Sync = new();

    public static string FilePath { get; } = Path.Combine(AppPaths.LogsDir, "error.log");

    public static void Write(string context, Exception ex) =>
        Write($"{context}: {ex.GetType().Name}: {ex.Message}{Environment.NewLine}{ex.StackTrace}");

    public static void Write(string message)
    {
        try
        {
            AppPaths.EnsureDirectories();
            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}";
            lock (Sync)
            {
                File.AppendAllText(FilePath, line);
            }
        }
        catch
        {
        }
    }
}
