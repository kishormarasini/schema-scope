using Spectre.Console;

namespace SchemaScope.Ui;

internal static class Banner
{
    public static void Title()
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine(
            $"  [{Theme.Brand}]▎[/] [bold {Theme.Title}]{AppInfo.Name}[/]" +
            $"  [{Theme.Subtle}]·[/]  " +
            $"[{Theme.Accent}]v{Theme.Escape(AppInfo.Version)}[/]");
        AnsiConsole.MarkupLine(
            $"  [{Theme.Brand}]▎[/] [{Theme.Muted}]{Theme.Escape(AppInfo.Tagline)}[/]");
        AnsiConsole.WriteLine();
    }

    public static void Section(string title, bool showBackHint = false)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine(
            $"  [{Theme.Brand}]▎[/] [bold {Theme.Title}]{Theme.Escape(title)}[/]");
        if (showBackHint)
        {
            AnsiConsole.MarkupLine(
                $"  [{Theme.Brand}]▎[/] [{Theme.Muted}]Type 'back' at any prompt to return.[/]");
        }
        AnsiConsole.WriteLine();
    }

    public static void StartupInfo(
        string server,
        string versionFolder,
        string? configSource = null)
    {
        var body = new System.Text.StringBuilder();

        if (!string.IsNullOrWhiteSpace(configSource))
        {
            body.AppendLine($"[{Theme.Brand}]●[/]  [{Theme.Subtitle}]Config[/]   [{Theme.Muted}]{Theme.Escape(configSource)}[/]");
        }

        body.Append($"[{Theme.Brand}]●[/]  [{Theme.Subtitle}]Server[/]   [{Theme.Info}]{Theme.Escape(server)}[/]");
        body.AppendLine();
        body.Append($"[{Theme.Brand}]●[/]  [{Theme.Subtitle}]Scripts[/]  [{Theme.Muted}]{Theme.Escape(versionFolder)}[/]");

        var panel = new Panel(new Markup(body.ToString()))
        {
            Border = BoxBorder.Rounded,
            BorderStyle = Theme.BorderStyle,
            Padding = new Padding(2, 0, 2, 0),
            Header = new PanelHeader($"[{Theme.Brand}] connection [/]", Justify.Left),
            Expand = false
        };
        AnsiConsole.Write(panel);
        AnsiConsole.WriteLine();
    }

    public static void ConnectionInfo(
        string server,
        string database,
        string versionFolder,
        string logPath)
    {
        var body = new System.Text.StringBuilder();
        body.Append($"[{Theme.Brand}]●[/]  [{Theme.Subtitle}]Server[/]    [{Theme.Info}]{Theme.Escape(server)}[/]");
        body.AppendLine();
        body.Append($"[{Theme.Brand}]●[/]  [{Theme.Subtitle}]Database[/]  [{Theme.Info}]{Theme.Escape(database)}[/]");
        body.AppendLine();
        body.Append($"[{Theme.Brand}]●[/]  [{Theme.Subtitle}]Scripts[/]   [{Theme.Muted}]{Theme.Escape(versionFolder)}[/]");
        body.AppendLine();
        body.Append($"[{Theme.Brand}]●[/]  [{Theme.Subtitle}]Log[/]       [{Theme.Muted}]{Theme.Escape(logPath)}[/]");

        var panel = new Panel(new Markup(body.ToString()))
        {
            Border = BoxBorder.Rounded,
            BorderStyle = Theme.BorderStyle,
            Padding = new Padding(2, 0, 2, 0),
            Header = new PanelHeader($"[{Theme.Brand}] connected [/]", Justify.Left),
            Expand = false
        };
        AnsiConsole.Write(panel);
        AnsiConsole.WriteLine();
    }
}
