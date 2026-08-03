using System.IO;
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

    public static void Home(SchemaScopeConfig config, int scriptCount, string? configSource)
    {
        AnsiConsole.Clear();
        AnsiConsole.WriteLine();
        Wordmark();
        AnsiConsole.WriteLine();
        StatusPills(config, scriptCount);
        AnsiConsole.WriteLine();
        InfoCards(config, scriptCount, configSource);
        AnsiConsole.WriteLine();
    }

    private static void Wordmark()
    {
        if (AnsiConsole.Profile.Width >= 100)
        {
            var figlet = new FigletText("SchemaScope")
                .LeftJustified()
                .Color(Theme.Hex(GradientFrom));
            AnsiConsole.Write(new Padder(figlet, new Padding(2, 0, 0, 0)));
            AnsiConsole.MarkupLine(
                $"  [{Theme.Muted}]{Theme.Escape(AppInfo.Tagline)}[/]" +
                $"   [{Theme.PillAccent}] v{Theme.Escape(AppInfo.Version)} [/]");
        }
        else
        {
            AnsiConsole.MarkupLine(
                $"  [bold]{Theme.Gradient("SchemaScope", GradientFrom, GradientTo)}[/]" +
                $"  [{Theme.PillAccent}] v{Theme.Escape(AppInfo.Version)} [/]");
            AnsiConsole.MarkupLine($"  [{Theme.Muted}]{Theme.Escape(AppInfo.Tagline)}[/]");
        }
        AnsiConsole.MarkupLine($"  {Theme.GradientRule(Math.Min(76, Math.Max(40, AnsiConsole.Profile.Width - 6)), GradientFrom, GradientTo)}");
    }

    private static void StatusPills(SchemaScopeConfig config, int scriptCount)
    {
        var auth = config.Connection.Authentication == AuthenticationMode.Windows
            ? "Windows auth"
            : $"SQL · {config.Connection.UserId}";

        var pills = new List<string>
        {
            $"[{Theme.PillBrand}] ⛁ {Theme.Escape(config.Database)} [/]",
            $"[{Theme.PillNeutral}] {Theme.Escape(config.Server)} [/]",
            $"[{Theme.PillAccent}] {scriptCount} script{(scriptCount == 1 ? "" : "s")} [/]",
            config.Connection.Encrypt
                ? $"[{Theme.PillOk}] {Theme.Escape(auth)} · TLS [/]"
                : $"[{Theme.PillNeutral}] {Theme.Escape(auth)} [/]"
        };

        AnsiConsole.MarkupLine("  " + string.Join(" ", pills));
    }

    private static void InfoCards(SchemaScopeConfig config, int scriptCount, string? configSource)
    {
        var connection = new Grid();
        connection.AddColumn(new GridColumn().NoWrap().PadRight(2));
        connection.AddColumn();
        connection.AddRow($"[{Theme.Subtitle}]Server[/]", $"[{Theme.Info}]{Theme.Escape(config.Server)}[/]");
        connection.AddRow($"[{Theme.Subtitle}]Database[/]", $"[{Theme.Info}]{Theme.Escape(config.Database)}[/]");
        connection.AddRow($"[{Theme.Subtitle}]Timeouts[/]",
            $"[{Theme.Muted}]connect {config.Connection.ConnectTimeoutSeconds}s · command {config.Connection.CommandTimeoutSeconds}s[/]");

        var workspace = new Grid();
        workspace.AddColumn(new GridColumn().NoWrap().PadRight(2));
        workspace.AddColumn();
        workspace.AddRow($"[{Theme.Subtitle}]Scripts[/]",
            $"[{Theme.Muted}]{Theme.Escape(Shorten(config.VersionFolder, 44))}[/]");
        workspace.AddRow($"[{Theme.Subtitle}]Scheme[/]",
            $"[{Theme.Accent}]{Theme.Escape(config.VersionScheme.LabelFormat)}[/]  [{Theme.Muted}]({scriptCount} matched)[/]");
        workspace.AddRow($"[{Theme.Subtitle}]Prepatch[/]", string.IsNullOrWhiteSpace(config.PrepatchFile)
            ? $"[{Theme.Subtle}]none[/]"
            : $"[{Theme.Muted}]{Theme.Escape(Path.GetFileName(config.PrepatchFile))}[/]");
        if (!string.IsNullOrWhiteSpace(configSource))
        {
            workspace.AddRow($"[{Theme.Subtitle}]Config[/]", $"[{Theme.Subtle}]{Theme.Escape(Shorten(configSource, 44))}[/]");
        }

        AnsiConsole.Write(new Padder(new Columns(
            Card(" connection ", connection),
            Card(" workspace ", workspace))
        {
            Expand = false
        }, new Padding(2, 0, 0, 0)));

        static Panel Card(string header, Grid body) => new(body)
        {
            Border = BoxBorder.Rounded,
            BorderStyle = Theme.BorderStyle,
            Padding = new Padding(2, 0, 2, 0),
            Header = new PanelHeader($"[{Theme.Brand}]{header}[/]", Justify.Left),
            Expand = false
        };
    }

    private static string Shorten(string path, int max)
    {
        if (string.IsNullOrEmpty(path) || path.Length <= max)
        {
            return path;
        }
        return "…" + path[^(max - 1)..];
    }

    public static void Section(string title, bool showBackHint = false)
    {
        AnsiConsole.Clear();
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine(
            $"  [bold]{Theme.Gradient("SchemaScope", GradientFrom, GradientTo)}[/] [{Theme.Subtle}]›[/] [bold {Theme.Title}]{Theme.Escape(title)}[/]" +
            (showBackHint ? $"   [{Theme.Subtle}]'back' at any prompt returns to the menu[/]" : string.Empty));
        AnsiConsole.MarkupLine($"  {Theme.GradientRule(Math.Min(76, Math.Max(40, AnsiConsole.Profile.Width - 6)), GradientFrom, GradientTo)}");
        AnsiConsole.WriteLine();
    }

    public static void SubSection(string title)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"  [{Theme.Brand}]▎[/] [bold {Theme.Title}]{Theme.Escape(title)}[/]");
        AnsiConsole.WriteLine();
    }

    public static void Goodbye()
    {
        AnsiConsole.Clear();
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"  [bold]{Theme.Gradient("SchemaScope", GradientFrom, GradientTo)}[/]  [{Theme.Muted}]— bye[/]");
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
