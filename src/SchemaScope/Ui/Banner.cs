using SchemaScope.Configuration;
using Spectre.Console;

namespace SchemaScope.Ui;

internal static class Banner
{
    private const string GradientFrom = "#89b4fa";
    private const string GradientTo   = "#cba6f7";

    public static void Title()
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine(
            $"  [bold]{Theme.Gradient("SchemaScope", GradientFrom, GradientTo)}[/]" +
            $"  [{Theme.PillAccent}] v{Theme.Escape(AppInfo.Version)} [/]");
        AnsiConsole.MarkupLine($"  [{Theme.Muted}]{Theme.Escape(AppInfo.Tagline)}[/]");
        AnsiConsole.MarkupLine($"  {Theme.GradientRule(46, GradientFrom, GradientTo)}");
        AnsiConsole.WriteLine();
    }

    public static void Section(string title, bool showBackHint = false)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine(
            $"  [{Theme.Brand}]▎[/] [bold {Theme.Title}]{Theme.Escape(title)}[/]" +
            (showBackHint ? $"   [{Theme.Subtle}]'back' at any prompt returns to the menu[/]" : string.Empty));
        AnsiConsole.WriteLine();
    }

    public static void StartupInfo(SchemaScopeConfig config, int scriptCount, string? configSource = null)
    {
        var auth = config.Connection.Authentication == AuthenticationMode.Windows
            ? "Windows Authentication"
            : $"SQL login · {config.Connection.UserId}";

        var grid = new Grid();
        grid.AddColumn(new GridColumn().NoWrap().PadRight(3));
        grid.AddColumn();

        grid.AddRow($"[{Theme.Subtitle}]Server[/]", $"[{Theme.Info}]{Theme.Escape(config.Server)}[/]");
        grid.AddRow($"[{Theme.Subtitle}]Database[/]", $"[{Theme.Info}]{Theme.Escape(config.Database)}[/]");
        grid.AddRow($"[{Theme.Subtitle}]Auth[/]", $"[white]{Theme.Escape(auth)}[/]" +
            (config.Connection.Encrypt ? $"  [{Theme.Success}]TLS ✓[/]" : string.Empty));
        grid.AddRow($"[{Theme.Subtitle}]Scripts[/]",
            $"[{Theme.Muted}]{Theme.Escape(config.VersionFolder)}[/]  [{Theme.Accent}]{scriptCount} file{(scriptCount == 1 ? "" : "s")}[/]");
        if (!string.IsNullOrWhiteSpace(configSource))
        {
            grid.AddRow($"[{Theme.Subtitle}]Config[/]", $"[{Theme.Subtle}]{Theme.Escape(configSource)}[/]");
        }

        var panel = new Panel(grid)
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
        var grid = new Grid();
        grid.AddColumn(new GridColumn().NoWrap().PadRight(3));
        grid.AddColumn();

        grid.AddRow($"[{Theme.Subtitle}]Server[/]", $"[{Theme.Info}]{Theme.Escape(server)}[/]");
        grid.AddRow($"[{Theme.Subtitle}]Database[/]", $"[{Theme.Info}]{Theme.Escape(database)}[/]");
        grid.AddRow($"[{Theme.Subtitle}]Scripts[/]", $"[{Theme.Muted}]{Theme.Escape(versionFolder)}[/]");
        grid.AddRow($"[{Theme.Subtitle}]Log[/]", $"[{Theme.Muted}]{Theme.Escape(logPath)}[/]");

        var panel = new Panel(grid)
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
