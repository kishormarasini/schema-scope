using System.IO;
using Microsoft.Data.SqlClient;
using SchemaScope.Configuration;
using SchemaScope.Sql;
using Spectre.Console;

namespace SchemaScope.Ui;

internal static class SetupWizard
{
    private const string BackChoice = "__back__";

    public static bool Run(SchemaScopeConfig config, bool firstRun)
    {
        Intro(firstRun);

        try
        {
            while (true)
            {
                AskServer(config);
                AskAuthentication(config);
                AskEncryption(config);

                var databases = TestConnection(config, out var connected);
                if (!connected && !AskContinueWithoutConnection())
                {
                    continue;
                }

                AskDatabase(config, databases);
                AskScriptsFolder(config);
                AskVersionScheme(config);
                AskPrepatch(config);

                ShowSummary(config, connected);
                if (!Prompts.AskYesNo("Save this configuration?", true))
                {
                    if (Prompts.AskYesNo("Start over?", true))
                    {
                        continue;
                    }
                    return false;
                }

                config.Save();
                AnsiConsole.MarkupLine(
                    $"  [{Theme.Success}]✓[/] Configuration saved to [{Theme.Muted}]{Theme.Escape(config.Path ?? SchemaScopeConfig.DefaultPath)}[/]");
                AnsiConsole.WriteLine();
                return true;
            }
        }
        catch (ReturnToMenuException)
        {
            AnsiConsole.MarkupLine($"  [{Theme.Muted}]setup cancelled[/]");
            return false;
        }
    }

    private static void Intro(bool firstRun)
    {
        AnsiConsole.WriteLine();
        var title = firstRun ? "Welcome to SchemaScope" : "Settings";
        var subtitle = firstRun
            ? "Let's get you connected. This takes about a minute and is saved for next time."
            : "Review and update the connection, database, and script settings.";

        var panel = new Panel(new Markup(
            $"[bold {Theme.Title}]{Theme.Escape(title)}[/]\n" +
            $"[{Theme.Muted}]{Theme.Escape(subtitle)}[/]\n" +
            $"[{Theme.Subtle}]Type 'back' at any prompt to cancel.[/]"))
        {
            Border = BoxBorder.Rounded,
            BorderStyle = Theme.BorderStyle,
            Padding = new Padding(2, 1, 2, 1),
            Expand = false
        };
        AnsiConsole.Write(panel);
        AnsiConsole.WriteLine();
    }

    private static void StepHeader(int step, string title)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine(
            $"  [{Theme.PillBrand}] {step}/6 [/] [bold {Theme.Title}]{Theme.Escape(title)}[/]");
    }

    private static void AskServer(SchemaScopeConfig config)
    {
        StepHeader(1, "Server");
        AnsiConsole.MarkupLine($"  [{Theme.Muted}]Examples: MACHINE\\INSTANCE · localhost · . · server.domain.com,1433[/]");
        config.Server = Prompts.AskRequired(
            "SQL Server instance",
            string.IsNullOrWhiteSpace(config.Server) ? null : config.Server);
    }

    private static void AskAuthentication(SchemaScopeConfig config)
    {
        StepHeader(2, "Authentication");

        const string WinAuth = "windows";
        const string SqlAuth = "sql";

        var choices = new List<string>();
        if (OperatingSystem.IsWindows())
        {
            choices.Add(WinAuth);
        }
        choices.Add(SqlAuth);
        choices.Add(BackChoice);

        var prompt = new SelectionPrompt<string>
        {
            HighlightStyle = Theme.HighlightStyle,
            PageSize = 5
        }
        .Title($"  [{Theme.Prompt}]How do you sign in?[/]")
        .UseConverter(c => c switch
        {
            WinAuth => Theme.MenuRow("⊞", "Windows Authentication", "Use your current Windows identity (recommended on Windows)"),
            SqlAuth => Theme.MenuRow("🔑", "SQL Server login", "User name and password"),
            _       => Theme.MenuRow("←", "Cancel", "Leave setup")
        })
        .AddChoices(choices);

        var picked = AnsiConsole.Prompt(prompt);
        if (picked == BackChoice)
        {
            throw new ReturnToMenuException();
        }

        if (picked == WinAuth)
        {
            config.Connection.Authentication = AuthenticationMode.Windows;
            return;
        }

        config.Connection.Authentication = AuthenticationMode.SqlPassword;
        config.Connection.UserId = Prompts.AskRequired(
            "User name",
            string.IsNullOrWhiteSpace(config.Connection.UserId) ? null : config.Connection.UserId);

        AskPassword(config.Connection);
    }

    private static void AskPassword(ConnectionSettings connection)
    {
        if (connection.PasswordFromEnvironment)
        {
            AnsiConsole.MarkupLine(
                $"  [{Theme.Success}]✓[/] Password found in [{Theme.Accent}]{ConnectionSettings.PasswordEnvVar}[/] — nothing to type, nothing stored on disk.");
            connection.PersistPassword = false;
            return;
        }

        const string EnvVar  = "env";
        const string Session = "session";
        const string Store   = "store";

        var prompt = new SelectionPrompt<string>
        {
            HighlightStyle = Theme.HighlightStyle,
            PageSize = 5
        }
        .Title($"  [{Theme.Prompt}]Where should the password live?[/]")
        .UseConverter(c => c switch
        {
            EnvVar  => Theme.MenuRow("★", "Environment variable", $"Set {ConnectionSettings.PasswordEnvVar} — most secure, great for CI"),
            Session => Theme.MenuRow("◌", "This session only", "Asked once per run, never written to disk"),
            Store   => Theme.MenuRow("⚠", "Save in config file", "Stored as plain text in your per-user config"),
            _       => c
        })
        .AddChoices(EnvVar, Session, Store);

        switch (AnsiConsole.Prompt(prompt))
        {
            case EnvVar:
                connection.PersistPassword = false;
                connection.Password = string.Empty;
                AnsiConsole.MarkupLine($"  [{Theme.Muted}]Set it before launching, e.g.[/]");
                AnsiConsole.MarkupLine(OperatingSystem.IsWindows()
                    ? $"  [{Theme.Info}]setx {ConnectionSettings.PasswordEnvVar} \"your-password\"[/]"
                    : $"  [{Theme.Info}]export {ConnectionSettings.PasswordEnvVar}=\"your-password\"[/]");
                if (Prompts.AskYesNo("Enter the password for this session too (so setup can test the connection)?", true))
                {
                    connection.Password = Prompts.AskSecret("Password");
                }
                break;

            case Session:
                connection.PersistPassword = false;
                connection.Password = Prompts.AskSecret("Password");
                break;

            case Store:
                connection.PersistPassword = true;
                connection.Password = Prompts.AskSecret("Password");
                AnsiConsole.MarkupLine(
                    $"  [{Theme.Warning}]The password will be saved as plain text. Prefer {ConnectionSettings.PasswordEnvVar} when possible.[/]");
                break;
        }
    }

    private static void AskEncryption(SchemaScopeConfig config)
    {
        config.Connection.Encrypt = Prompts.AskYesNo("Encrypt the connection (TLS)?", config.Connection.Encrypt);
        if (config.Connection.Encrypt)
        {
            config.Connection.TrustServerCertificate = Prompts.AskYesNo(
                "Trust the server certificate (needed for self-signed certs)?",
                config.Connection.TrustServerCertificate);
        }
    }

    private static IReadOnlyList<string> TestConnection(SchemaScopeConfig config, out bool connected)
    {
        StepHeader(3, "Connection check");

        if (config.Connection.Authentication == AuthenticationMode.SqlPassword
            && !config.Connection.HasEffectivePassword)
        {
            AnsiConsole.MarkupLine($"  [{Theme.Warning}]No password available yet — skipping the live check.[/]");
            connected = false;
            return Array.Empty<string>();
        }

        var factory = new SqlConnectionFactory(config.Server, config.Connection);
        string? error = null;
        var databases = new List<string>();

        AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(Theme.HighlightStyle)
            .Start($"Connecting to [bold]{Theme.Escape(config.Server)}[/] …", _ =>
            {
                try
                {
                    using var conn = factory.Open("master");
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = "SELECT name FROM sys.databases WHERE database_id > 4 AND state = 0 ORDER BY name";
                    using var reader = cmd.ExecuteReader();
                    while (reader.Read())
                    {
                        databases.Add(reader.GetString(0));
                    }
                }
                catch (Exception ex) when (ex is SqlException or InvalidOperationException or IOException)
                {
                    error = ex.Message;
                }
            });

        if (error is null)
        {
            AnsiConsole.MarkupLine(
                $"  [{Theme.Success}]✓[/] Connected — [{Theme.Info}]{databases.Count}[/] user database{(databases.Count == 1 ? "" : "s")} visible.");
            connected = true;
            return databases;
        }

        AnsiConsole.MarkupLine($"  [{Theme.Danger}]✗ Could not connect:[/]");
        AnsiConsole.MarkupLine($"  [{Theme.Muted}]{Theme.Escape(error)}[/]");
        connected = false;
        return Array.Empty<string>();
    }

    private static bool AskContinueWithoutConnection()
    {
        const string Edit = "edit";
        const string Keep = "keep";

        var prompt = new SelectionPrompt<string>
        {
            HighlightStyle = Theme.HighlightStyle,
            PageSize = 4
        }
        .Title($"  [{Theme.Prompt}]What next?[/]")
        .UseConverter(c => c switch
        {
            Edit => Theme.MenuRow("↺", "Edit connection details", "Go back and fix the server or credentials"),
            Keep => Theme.MenuRow("→", "Continue anyway", "Finish setup now, connect later"),
            _    => Theme.MenuRow("←", "Cancel setup", "Leave without saving")
        })
        .AddChoices(Edit, Keep, BackChoice);

        var picked = AnsiConsole.Prompt(prompt);
        if (picked == BackChoice)
        {
            throw new ReturnToMenuException();
        }
        return picked == Keep;
    }

    private static void AskDatabase(SchemaScopeConfig config, IReadOnlyList<string> databases)
    {
        StepHeader(4, "Default database");

        const string Manual = "__manual__";

        if (databases.Count == 0)
        {
            config.Database = Prompts.AskRequired(
                "Default database",
                string.IsNullOrWhiteSpace(config.Database) ? null : config.Database);
            return;
        }

        var choices = new List<string>(databases) { Manual };
        var prompt = new SelectionPrompt<string>
        {
            HighlightStyle = Theme.HighlightStyle,
            PageSize = 12,
            SearchEnabled = true
        }
        .Title($"  [{Theme.Prompt}]Pick the default database[/]  [{Theme.Muted}](type to filter)[/]")
        .UseConverter(c => c == Manual
            ? Theme.MenuRow("…", "Type a name", "Enter a database name manually")
            : (c == config.Database
                ? $"[bold]{Markup.Escape(c)}[/]  [{Theme.Accent}](current)[/]"
                : Markup.Escape(c)))
        .AddChoices(choices);

        var picked = AnsiConsole.Prompt(prompt);
        config.Database = picked == Manual
            ? Prompts.AskRequired("Database name", string.IsNullOrWhiteSpace(config.Database) ? null : config.Database)
            : picked;
    }

    private static void AskScriptsFolder(SchemaScopeConfig config)
    {
        StepHeader(5, "Migration scripts");
        AnsiConsole.MarkupLine($"  [{Theme.Muted}]The folder holding your versioned .sql files.[/]");

        config.VersionFolder = Prompts.AskValidated(
            "Scripts folder",
            string.IsNullOrWhiteSpace(config.VersionFolder) ? null : config.VersionFolder,
            path => Directory.Exists(path) ? null : $"Folder does not exist: {path}",
            Prompts.NormalizePath);
    }

    private static void AskVersionScheme(SchemaScopeConfig config)
    {
        var count = CountMatching(config.VersionFolder, config.VersionScheme);
        AnsiConsole.MarkupLine(
            $"  [{Theme.Success}]✓[/] [{Theme.Info}]{count}[/] script{(count == 1 ? "" : "s")} match the [{Theme.Accent}]{Theme.Escape(config.VersionScheme.LabelFormat)}[/] naming scheme.");

        if (count > 0 && !Prompts.AskYesNo("Change the file naming scheme?", false))
        {
            return;
        }

        const string SchemeDotted = "dotted";
        const string SchemeFlyway = "flyway";
        const string SchemeCustom = "custom";
        const string SchemeKeep   = "keep";

        while (true)
        {
            var prompt = new SelectionPrompt<string>
            {
                HighlightStyle = Theme.HighlightStyle,
                PageSize = 6
            }
            .Title($"  [{Theme.Prompt}]How are your script files named?[/]")
            .UseConverter(c => c switch
            {
                SchemeDotted => Theme.MenuRow("◈", "1.0.0.N.sql", "Dotted build numbers: 1.0.0.1.sql, 1.0.0.2.sql, …"),
                SchemeFlyway => Theme.MenuRow("◈", "VN__description.sql", "Flyway style: V1__init.sql, V2__add_users.sql, …"),
                SchemeCustom => Theme.MenuRow("✎", "Custom pattern", "Provide your own regex and formats"),
                _            => Theme.MenuRow("←", "Keep current", $"Stay on {config.VersionScheme.LabelFormat}")
            })
            .AddChoices(SchemeDotted, SchemeFlyway, SchemeCustom, SchemeKeep);

            var scheme = new VersionScheme();
            switch (AnsiConsole.Prompt(prompt))
            {
                case SchemeDotted:
                    scheme.FilePattern    = @"^1\.0\.0\.(\d+)\.sql$";
                    scheme.FileNameFormat = "1.0.0.{0}.sql";
                    scheme.LabelFormat    = "1.0.0.{0}";
                    break;

                case SchemeFlyway:
                    scheme.FilePattern    = @"^V(\d+)__.*\.sql$";
                    scheme.FileNameFormat = "V{0}__migration.sql";
                    scheme.LabelFormat    = "V{0}";
                    break;

                case SchemeCustom:
                    scheme.FilePattern = Prompts.AskValidated(
                        "Regex with one capture group for the version number",
                        config.VersionScheme.FilePattern,
                        ValidatePattern);
                    scheme.FileNameFormat = Prompts.AskRequired("File name format ({0} = number)", config.VersionScheme.FileNameFormat);
                    scheme.LabelFormat    = Prompts.AskRequired("Display label format ({0} = number)", config.VersionScheme.LabelFormat);
                    break;

                default:
                    return;
            }

            var matched = CountMatching(config.VersionFolder, scheme);
            AnsiConsole.MarkupLine(
                $"  [{(matched > 0 ? Theme.Success : Theme.Warning)}]{(matched > 0 ? "✓" : "▲")}[/] [{Theme.Info}]{matched}[/] script{(matched == 1 ? "" : "s")} match this scheme.");

            if (matched == 0 && !Prompts.AskYesNo("No files match. Use this scheme anyway?", false))
            {
                continue;
            }

            config.VersionScheme = scheme;
            return;
        }
    }

    private static string? ValidatePattern(string pattern)
    {
        try
        {
            var regex = new System.Text.RegularExpressions.Regex(pattern);
            return regex.GetGroupNumbers().Length < 2
                ? "The regex needs exactly one capturing group for the version number."
                : null;
        }
        catch (ArgumentException ex)
        {
            return $"Invalid regex: {ex.Message}";
        }
    }

    private static int CountMatching(string folder, VersionScheme scheme)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            {
                return 0;
            }
            var locator = new VersionScriptLocator(folder, scheme);
            return locator.GetInRange(0, 0).Count;
        }
        catch (ArgumentException)
        {
            return 0;
        }
    }

    private static void AskPrepatch(SchemaScopeConfig config)
    {
        StepHeader(6, "Prepatch (optional)");
        AnsiConsole.MarkupLine($"  [{Theme.Muted}]An idempotent .sql file run before migrations. Press Enter to skip.[/]");

        var answer = Prompts.AskOptionalValidated(
            "Prepatch file",
            string.IsNullOrWhiteSpace(config.PrepatchFile) ? null : config.PrepatchFile,
            path => File.Exists(path) ? null : $"File does not exist: {path}",
            Prompts.NormalizePath);

        config.PrepatchFile = answer ?? string.Empty;
    }

    private static void ShowSummary(SchemaScopeConfig config, bool connected)
    {
        AnsiConsole.WriteLine();

        var auth = config.Connection.Authentication == AuthenticationMode.Windows
            ? "Windows Authentication"
            : $"SQL login · {config.Connection.UserId}";

        var passwordSource = config.Connection.Authentication != AuthenticationMode.SqlPassword
            ? null
            : config.Connection.PasswordFromEnvironment
                ? $"env var {ConnectionSettings.PasswordEnvVar}"
                : config.Connection.PersistPassword
                    ? "stored in config file"
                    : config.Connection.HasEffectivePassword
                        ? "this session only"
                        : "asked at startup";

        var grid = new Grid();
        grid.AddColumn(new GridColumn().NoWrap().PadRight(3));
        grid.AddColumn();

        void Row(string label, string valueMarkup) =>
            grid.AddRow($"[{Theme.Subtitle}]{label}[/]", valueMarkup);

        Row("Server", $"[{Theme.Info}]{Theme.Escape(config.Server)}[/]");
        Row("Auth", Theme.Escape(auth));
        if (passwordSource is not null)
        {
            Row("Password", $"[{Theme.Muted}]{Theme.Escape(passwordSource)}[/]");
        }
        Row("Encrypt", config.Connection.Encrypt ? $"[{Theme.Success}]on[/]" : $"[{Theme.Muted}]off[/]");
        Row("Database", $"[{Theme.Info}]{Theme.Escape(config.Database)}[/]");
        Row("Scripts", $"[{Theme.Muted}]{Theme.Escape(config.VersionFolder)}[/]");
        Row("Scheme", $"[{Theme.Accent}]{Theme.Escape(config.VersionScheme.LabelFormat)}[/]");
        Row("Prepatch", string.IsNullOrWhiteSpace(config.PrepatchFile)
            ? $"[{Theme.Subtle}]none[/]"
            : $"[{Theme.Muted}]{Theme.Escape(config.PrepatchFile)}[/]");
        Row("Connection", connected ? $"[{Theme.Success}]verified ✓[/]" : $"[{Theme.Warning}]not verified[/]");

        var panel = new Panel(grid)
        {
            Border = BoxBorder.Rounded,
            BorderStyle = Theme.BorderStyle,
            Padding = new Padding(2, 1, 2, 1),
            Header = new PanelHeader($"[{Theme.Brand}] summary [/]", Justify.Left),
            Expand = false
        };
        AnsiConsole.Write(panel);
    }
}
