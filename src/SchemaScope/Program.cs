using System.IO;
using System.Text;
using SchemaScope;
using SchemaScope.Cli;
using SchemaScope.Configuration;
using SchemaScope.Sql;
using SchemaScope.Ui;
using Spectre.Console;

Console.OutputEncoding = Encoding.UTF8;

try
{
    return Execute(args);
}
catch (Exception ex)
{
    ErrorLog.Write("Unhandled fatal error", ex);
    AnsiConsole.MarkupLine($"[{Theme.Danger}]Unexpected error: {Markup.Escape(ex.Message)}[/]");
    AnsiConsole.MarkupLine($"[{Theme.Muted}]Details written to {Markup.Escape(ErrorLog.FilePath)}[/]");
    return 1;
}

static int Execute(string[] args)
{
AppPaths.EnsureDirectories();

CliOptions options;
try
{
    options = CliOptions.Parse(args);
}
catch (ArgumentException ex)
{
    AnsiConsole.MarkupLine($"[{Theme.Danger}]{Markup.Escape(ex.Message)}[/]");
    PrintHelp();
    return 2;
}

if (options.ShowVersion)
{
    AnsiConsole.MarkupLine($"[{Theme.Title}]{AppInfo.Name}[/] [{Theme.Accent}]{AppInfo.Version}[/]");
    AnsiConsole.MarkupLine($"[{Theme.Subtitle}]Developed By:[/] [{Theme.Brand}]{AppInfo.Author}[/]");
    AnsiConsole.MarkupLine($"[{Theme.Subtitle}]License:[/] [{Theme.Brand}]{AppInfo.License}[/]");
    return 0;
}

if (options.ShowHelp)
{
    PrintHelp();
    return 0;
}

var config = SchemaScopeConfig.Load(options.ConfigPath);

if (!OperatingSystem.IsWindows() && config.Connection.Authentication == AuthenticationMode.Windows)
{
    AnsiConsole.MarkupLine(
        $"[{Theme.Warning}]Windows Authentication is selected but the OS is not Windows. On Linux/macOS set \"Authentication\": \"SqlPassword\" (with UserId/Password) in the config file.[/]");
}

var server        = FirstNonEmpty(options.Server, config.Server);
var databaseName  = FirstNonEmpty(options.DatabaseName, config.Database);
var versionFolder = FirstNonEmpty(options.VersionFolder, config.VersionFolder);
var prepatchFile  = FirstNonEmpty(options.PrepatchFile, config.PrepatchFile);

if (options.IsInteractive)
{
    Banner.Title();

    if (string.IsNullOrWhiteSpace(server))
    {
        server = Prompts.AskRequired(@"SQL Server (e.g. MACHINE\INSTANCE or .)");
    }

    while (string.IsNullOrWhiteSpace(versionFolder) || !Directory.Exists(versionFolder))
    {
        var defaultFolder = Directory.Exists(versionFolder) ? versionFolder : null;
        AnsiConsole.MarkupLine(
            $"[{Theme.Muted}]folder containing your versioned .sql files (matching {Markup.Escape(config.VersionScheme.FileNameFormat)})[/]");
        versionFolder = Prompts.AskExistingFolder("Scripts folder", defaultFolder);
    }
}
else
{
    if (string.IsNullOrWhiteSpace(server))
    {
        AnsiConsole.MarkupLine($"[{Theme.Danger}]Server is required (pass --server or set it in the config file).[/]");
        return 2;
    }

    switch (options.Command)
    {
        case CliCommand.Backup:
            if (string.IsNullOrWhiteSpace(options.SourceDatabase))
            {
                AnsiConsole.MarkupLine($"[{Theme.Danger}]--backup requires --source <database>.[/]");
                return 2;
            }
            if (string.IsNullOrWhiteSpace(options.BackupPath))
            {
                AnsiConsole.MarkupLine($"[{Theme.Danger}]--backup requires --backup-path <path>.[/]");
                return 2;
            }
            break;

        case CliCommand.Restore:
            if (string.IsNullOrWhiteSpace(options.TargetDatabase))
            {
                AnsiConsole.MarkupLine($"[{Theme.Danger}]--restore requires --target <database>.[/]");
                return 2;
            }
            if (string.IsNullOrWhiteSpace(options.BackupPath))
            {
                AnsiConsole.MarkupLine($"[{Theme.Danger}]--restore requires --backup-path <path>.[/]");
                return 2;
            }
            break;

        case CliCommand.Clone:
            if (string.IsNullOrWhiteSpace(options.SourceDatabase) || string.IsNullOrWhiteSpace(options.TargetDatabase))
            {
                AnsiConsole.MarkupLine($"[{Theme.Danger}]--clone requires --source <database> and --target <database>.[/]");
                return 2;
            }
            break;

        default:
            if (string.IsNullOrWhiteSpace(databaseName))
            {
                AnsiConsole.MarkupLine($"[{Theme.Danger}]--database is required in non-interactive mode.[/]");
                return 2;
            }
            if (string.IsNullOrWhiteSpace(versionFolder))
            {
                AnsiConsole.MarkupLine($"[{Theme.Danger}]Version folder is required (pass --version-folder or set it in the config file).[/]");
                return 2;
            }
            break;
    }
}

if (string.IsNullOrWhiteSpace(server))
{
    AnsiConsole.MarkupLine($"[{Theme.Danger}]Missing required connection details.[/]");
    return 2;
}

var locator = new VersionScriptLocator(versionFolder, config.VersionScheme);
var needsScriptsFolder = options.Command is CliCommand.Auto or CliCommand.Verify or CliCommand.Detect;
if (needsScriptsFolder && !locator.FolderExists)
{
    AnsiConsole.MarkupLine($"[{Theme.Danger}]Scripts folder not found: {Markup.Escape(versionFolder)}[/]");
    return 2;
}

var factory = new SqlConnectionFactory(server, config.Connection);

if (!SqlSession.TryTestConnection(factory, "master", out var connectError))
{
    AnsiConsole.MarkupLine($"[{Theme.Danger}]Cannot connect to {Markup.Escape(server)}:[/]");
    AnsiConsole.MarkupLine($"[{Theme.Muted}]{Markup.Escape(connectError ?? "(no details)")}[/]");
    return 3;
}

config.Server = server;
if (!string.IsNullOrWhiteSpace(versionFolder))
{
    config.VersionFolder = versionFolder;
}
config.PrepatchFile = prepatchFile;
if (!string.IsNullOrWhiteSpace(databaseName))
{
    config.Database = databaseName;
}
config.Save();

var actions = new ToolkitActions(factory, locator, prepatchFile, config.DefaultSchema);

Banner.StartupInfo(server, versionFolder, config.Path);

if (options.IsInteractive)
{
    var shell = new InteractiveShell(actions, config.Database);
    shell.Run();
    return 0;
}

switch (options.Command)
{
    case CliCommand.Backup:
        return actions.RunBackup(options.SourceDatabase!, options.BackupPath!) ? 0 : 1;

    case CliCommand.Restore:
        return actions.RunRestore(options.TargetDatabase!, options.BackupPath!, options.DataDir, options.LogDir) ? 0 : 1;

    case CliCommand.Clone:
        return actions.RunClone(options.SourceDatabase!, options.TargetDatabase!, options.BackupPath, options.DataDir, options.LogDir) ? 0 : 1;

    case CliCommand.Verify:
        return actions.VerifyForCi(databaseName, options.VerifyTarget);

    case CliCommand.Detect:
        actions.DetectCurrentVersion(databaseName, options.StartFrom);
        return 0;

    default:
        if (options.PrepatchOnly)
        {
            actions.RunPrepatch(databaseName);
        }
        else if (options.SkipPrepatch || string.IsNullOrWhiteSpace(prepatchFile))
        {
            actions.RunVersionRange(databaseName, options.StartFrom, options.EndAt);
        }
        else
        {
            actions.RunPrepatchAndVersionRange(databaseName, options.StartFrom, options.EndAt);
        }
        return 0;
}
}

static string FirstNonEmpty(params string?[] values)
{
    foreach (var v in values)
    {
        if (!string.IsNullOrWhiteSpace(v))
        {
            return v!;
        }
    }
    return string.Empty;
}

static void PrintHelp()
{
    AnsiConsole.MarkupLine($"[{Theme.Title}]SchemaScope[/]");
    AnsiConsole.MarkupLine($"[{Theme.Muted}]SQL Server schema runner and verifier[/]");
    AnsiConsole.WriteLine();
    AnsiConsole.MarkupLine($"[{Theme.Muted}]Usage (interactive):[/]");
    AnsiConsole.WriteLine("  schemascope");
    AnsiConsole.WriteLine();
    AnsiConsole.MarkupLine($"[{Theme.Muted}]Usage (scripted):[/]");
    AnsiConsole.WriteLine("  schemascope --database <name> [--server <host>] [--version-folder <path>]");
    AnsiConsole.WriteLine("              [--config <path>] [--prepatch-file <path>] [--start-from <n>] [--end-at <n>]");
    AnsiConsole.WriteLine("              [--skip-prepatch | --prepatch-only]");
    AnsiConsole.WriteLine();
    AnsiConsole.MarkupLine($"[{Theme.Muted}]Usage (operations):[/]");
    AnsiConsole.WriteLine("  schemascope --verify <n> --database <name> [--version-folder <path>]");
    AnsiConsole.WriteLine("  schemascope --detect --database <name> [--start-from <n>]");
    AnsiConsole.WriteLine("  schemascope --backup  --source <db> --backup-path <file.bak>");
    AnsiConsole.WriteLine("  schemascope --restore --target <db> --backup-path <file.bak> [--data-dir <d>] [--log-dir <d>]");
    AnsiConsole.WriteLine("  schemascope --clone   --source <db> --target <db> [--backup-path <file.bak>]");
    AnsiConsole.WriteLine();
    AnsiConsole.MarkupLine($"[{Theme.Muted}]Options:[/]");
    AnsiConsole.MarkupLine("  -d, --database         Target DB. Passing this triggers non-interactive mode.");
    AnsiConsole.MarkupLine("  -s, --server           SQL instance (e.g. MACHINE\\INSTANCE or .).");
    AnsiConsole.MarkupLine("  -f, --version-folder   Folder containing the versioned .sql scripts.");
    AnsiConsole.MarkupLine("  -c, --config           Path to a config file (defaults to the per-user config).");
    AnsiConsole.MarkupLine("      --prepatch-file    Optional idempotent pre-step SQL file.");
    AnsiConsole.MarkupLine("      --start-from       Lowest version to run/scan. 0 = no lower bound.");
    AnsiConsole.MarkupLine("      --end-at           Highest version to run. 0 = no upper bound.");
    AnsiConsole.MarkupLine("      --skip-prepatch    Skip prepatch. Run version scripts only.");
    AnsiConsole.MarkupLine("      --prepatch-only    Run prepatch only. No version scripts.");
    AnsiConsole.WriteLine();
    AnsiConsole.MarkupLine($"[{Theme.Muted}]Operations:[/]");
    AnsiConsole.MarkupLine("      --verify <n>       Verify a version against the DB. Exit 0 = applied, 1 = drift (CI-friendly).");
    AnsiConsole.MarkupLine("      --detect           Detect the current applied version.");
    AnsiConsole.MarkupLine("      --backup           Back up a database. Needs --source and --backup-path.");
    AnsiConsole.MarkupLine("      --restore          Restore a backup. Needs --target and --backup-path.");
    AnsiConsole.MarkupLine("      --clone            Clone a database. Needs --source and --target.");
    AnsiConsole.MarkupLine("      --source <db>      Source database (backup / clone).");
    AnsiConsole.MarkupLine("      --target <db>      Target database (restore / clone).");
    AnsiConsole.MarkupLine("      --backup-path <p>  Path to the .bak file (backup / restore / clone).");
    AnsiConsole.MarkupLine("      --data-dir <p>     Override the restore data-file directory.");
    AnsiConsole.MarkupLine("      --log-dir <p>      Override the restore log-file directory.");
    AnsiConsole.MarkupLine("  -h, --help             Show this help.");
    AnsiConsole.MarkupLine("  -v, --version          Print version and exit.");
}
