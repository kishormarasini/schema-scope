using System.IO;
using Spectre.Console;

namespace SchemaScope.Ui;

public sealed class RunLogger : IDisposable
{
    private readonly StreamWriter? _writer;
    private readonly object _sync = new();

    public string LogFilePath { get; }

    private RunLogger(string logFilePath, StreamWriter? writer)
    {
        LogFilePath = logFilePath;
        _writer = writer;
    }

    public static RunLogger Create(string databaseName)
    {
        AppPaths.EnsureDirectories();

        var safeDb = SanitizeForPath(databaseName);
        var fileName = $"schemascope_{safeDb}_{DateTime.Now:yyyyMMdd_HHmmss}.log";
        var path = Path.Combine(AppPaths.LogsDir, fileName);

        StreamWriter? writer = null;
        try
        {
            writer = new StreamWriter(path, append: true) { AutoFlush = true };
        }
        catch (IOException ex)
        {
            AnsiConsole.MarkupLine($"[{Theme.Warning}]Could not open log file '{Spectre.Console.Markup.Escape(path)}': {Spectre.Console.Markup.Escape(ex.Message)}[/]");
        }

        return new RunLogger(path, writer);
    }

    public void Markup(string markup)
    {
        AnsiConsole.MarkupLine(markup);
        WriteToFile(Spectre.Console.Markup.Remove(markup));
    }

    public void Plain(string text)
    {
        AnsiConsole.WriteLine(text);
        WriteToFile(text);
    }

    public void Blank()
    {
        AnsiConsole.WriteLine();
        WriteToFile(string.Empty);
    }

    public void LogOnly(string text) => WriteToFile(text);

    public void Info(string text)    => Markup($"[{Theme.Info}]{Spectre.Console.Markup.Escape(text)}[/]");
    public void Success(string text) => Markup($"[{Theme.Success}]{Spectre.Console.Markup.Escape(text)}[/]");
    public void Warn(string text)    => Markup($"[{Theme.Warning}]{Spectre.Console.Markup.Escape(text)}[/]");
    public void Error(string text)   => Markup($"[{Theme.Danger}]{Spectre.Console.Markup.Escape(text)}[/]");
    public void Muted(string text)   => Markup($"[{Theme.Muted}]{Spectre.Console.Markup.Escape(text)}[/]");

    private void WriteToFile(string text)
    {
        if (_writer is null)
        {
            return;
        }

        lock (_sync)
        {
            try
            {
                _writer.WriteLine(text);
            }
            catch
            {
            }
        }
    }

    private static string SanitizeForPath(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "unknown";
        }

        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        return new string(chars);
    }

    public void Dispose()
    {
        lock (_sync)
        {
            _writer?.Dispose();
        }
    }
}
