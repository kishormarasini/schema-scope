using System.IO;
using SchemaScope.TestDb;
using Spectre.Console;

namespace SchemaScope.Ui;

internal sealed class InteractiveShell
{
    private readonly ToolkitActions _actions;
    private readonly string? _defaultDatabase;

    public InteractiveShell(ToolkitActions actions, string? defaultDatabase = null)
    {
        _actions = actions ?? throw new ArgumentNullException(nameof(actions));
        _defaultDatabase = string.IsNullOrWhiteSpace(defaultDatabase) ? null : defaultDatabase;
    }

    public void Run()
    {
        while (true)
        {
            var action = Prompts.AskMenuChoice();

            try
            {
                switch (action)
                {
                    case MenuAction.DetectVersion:
                        Banner.Section("Detect current version", showBackHint: true);
                        {
                            var dvDb = AskTargetDatabase();
                            var dvFrom = Prompts.AskInt("Start from version number (1 = full scan)", 1);
                            _actions.DetectCurrentVersion(dvDb, dvFrom);
                        }
                        break;

                    case MenuAction.VerifyVersion:
                        Banner.Section("Verify", showBackHint: true);
                        {
                            var vvDb = AskTargetDatabase();
                            _actions.VerifyVersion(vvDb, Prompts.AskInt("Version number to check", 0));
                        }
                        break;

                    case MenuAction.FullHeal:
                        Banner.Section("Full heal", showBackHint: true);
                        {
                            var fhDb = AskTargetDatabase();
                            var fhFrom = Prompts.AskInt("Start from version number", 1);
                            var fhTo   = Prompts.AskInt("End at version number", 9999);
                            _actions.RunPrepatchAndVersionRange(fhDb, fhFrom, fhTo);
                        }
                        break;

                    case MenuAction.VersionsOnly:
                        Banner.Section("Versions", showBackHint: true);
                        {
                            var voDb = AskTargetDatabase();
                            var voFrom = Prompts.AskInt("Start from version number", 1);
                            var voTo   = Prompts.AskInt("End at version number", 9999);
                            _actions.RunVersionRange(voDb, voFrom, voTo);
                        }
                        break;

                    case MenuAction.SpecificVersion:
                        Banner.Section("Specific version", showBackHint: true);
                        {
                            var svDb = AskTargetDatabase();
                            _actions.RunSpecificVersion(svDb, Prompts.AskInt("Version number", 0));
                        }
                        break;

                    case MenuAction.PrepatchOnly:
                        Banner.Section("Prepatch", showBackHint: true);
                        {
                            var ppDb = AskTargetDatabase();
                            _actions.RunPrepatch(ppDb);
                        }
                        break;

                    case MenuAction.Backup:
                        RunBackupFlow();
                        break;

                    case MenuAction.Restore:
                        RunRestoreFlow();
                        break;

                    case MenuAction.Clone:
                        RunCloneFlow();
                        break;

                    case MenuAction.TestDbScripts:
                        RunTestDbScriptsFlow();
                        break;

                    case MenuAction.Exit:
                        AnsiConsole.MarkupLine($"[{Theme.Muted}]bye[/]");
                        return;
                }
            }
            catch (ReturnToMenuException)
            {
                AnsiConsole.MarkupLine($"[{Theme.Muted}]returning to menu[/]");
            }

            AnsiConsole.WriteLine();
        }
    }

    private string AskTargetDatabase() =>
        Prompts.AskValidated("Target database", _defaultDatabase, ValidateDbIdentifier);

    private void RunBackupFlow()
    {
        Banner.Section("Backup", showBackHint: true);

        string sourceDb = string.Empty;

        while (true)
        {
            sourceDb = Prompts.AskValidated("Source database", sourceDb, ValidateDbIdentifier);
            var backupPath = AutoBackupPath(sourceDb);

            if (!TryEnsureDirectory(Path.GetDirectoryName(backupPath)))
            {
                return;
            }

            if (_actions.RunBackup(sourceDb, backupPath))
            {
                return;
            }
            if (!Prompts.AskYesNo("Backup failed. Retry with a different source?", true))
            {
                return;
            }
        }
    }

    private void RunRestoreFlow()
    {
        Banner.Section("Restore", showBackHint: true);

        string backupPath = PickBackupToRestore();
        string targetDb   = string.Empty;

        while (true)
        {
            if (string.IsNullOrWhiteSpace(targetDb))
            {
                var sourceDb = _actions.TryGetBackupSourceDatabase(backupPath);
                if (!string.IsNullOrWhiteSpace(sourceDb))
                {
                    targetDb = $"{sourceDb}_Restored";
                }
            }

            targetDb = Prompts.AskValidated("Target database", targetDb, ValidateDbIdentifier);

            if (_actions.RunRestore(targetDb, backupPath, dataDir: null, logDir: null))
            {
                return;
            }
            if (!Prompts.AskYesNo("Restore failed. Retry with edits?", true))
            {
                return;
            }
        }
    }

    private void RunCloneFlow()
    {
        Banner.Section("Clone", showBackHint: true);

        string sourceDb = string.Empty;
        string targetDb = string.Empty;

        while (true)
        {
            sourceDb = Prompts.AskValidated("Source database", sourceDb, ValidateDbIdentifier);
            targetDb = Prompts.AskValidated("Target database", targetDb, value =>
                ValidateDbIdentifier(value)
                ?? (string.Equals(value, sourceDb, StringComparison.OrdinalIgnoreCase)
                    ? "Target must differ from source."
                    : null));

            var autoPath = AutoBackupPath(sourceDb);
            if (!TryEnsureDirectory(Path.GetDirectoryName(autoPath)))
            {
                return;
            }

            if (_actions.RunClone(sourceDb, targetDb, autoPath, dataDir: null, logDir: null))
            {
                return;
            }
            if (!Prompts.AskYesNo("Clone failed. Retry with edits?", true))
            {
                return;
            }
        }
    }

    private static readonly System.Text.RegularExpressions.Regex SafeDbIdentifier =
        new(@"^[A-Za-z_][A-Za-z0-9_]*$", System.Text.RegularExpressions.RegexOptions.Compiled);

    private static string? ValidateDbIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Database name is required.";
        }
        if (!SafeDbIdentifier.IsMatch(value))
        {
            return "Database name must start with a letter or underscore and contain only letters, digits, or underscores.";
        }
        return null;
    }

    private static string? ValidateExistingBackupFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "Path is required.";
        }
        if (Directory.Exists(path))
        {
            return $"That path is a folder. Enter the path to the .bak file.";
        }
        if (!File.Exists(path))
        {
            return $"Backup file not found: {path}";
        }
        return null;
    }

    private static string AutoBackupPath(string sourceDb)
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "Backups");
        var fileName = $"{sourceDb}_{DateTime.Now:yyyyMMdd_HHmmss}.bak";
        return Path.Combine(dir, fileName);
    }

    private const string CustomBackupSentinel = "__custom__";
    private const string BackToMenuSentinel   = "__back__";

    private static string PickBackupToRestore()
    {
        var backups = ListBackups();

        if (backups.Count == 0)
        {
            AnsiConsole.MarkupLine(
                $"[{Theme.Muted}]No backups found in the app folder. Paste the full path to a .bak file below.[/]");
            return Prompts.AskValidated(
                "Full path to .bak file",
                null,
                ValidateExistingBackupFile,
                Prompts.NormalizePath);
        }

        var choices = new List<string>(backups.Select(b => b.FullName))
        {
            CustomBackupSentinel,
            BackToMenuSentinel
        };

        var prompt = new SelectionPrompt<string>
        {
            HighlightStyle = Theme.HighlightStyle,
            PageSize = 15
        }
        .Title($"[bold {Theme.Title}]Pick a backup to restore[/]  [{Theme.Muted}](newest first)[/]")
        .UseConverter(DescribeBackupChoice)
        .AddChoices(choices);

        var selected = AnsiConsole.Prompt(prompt);

        if (selected == BackToMenuSentinel)
        {
            throw new ReturnToMenuException();
        }
        if (selected == CustomBackupSentinel)
        {
            return Prompts.AskValidated(
                "Full path to .bak file",
                null,
                ValidateExistingBackupFile,
                Prompts.NormalizePath);
        }
        return selected;
    }

    private static string DescribeBackupChoice(string path)
    {
        if (path == CustomBackupSentinel)
        {
            return Theme.MenuRow("…", "Other",        "Paste a full path to a .bak file");
        }
        if (path == BackToMenuSentinel)
        {
            return Theme.MenuRow("←", "Back to menu", "Cancel and return to the main menu");
        }

        var fi = new FileInfo(path);
        if (!fi.Exists)
        {
            return Markup.Escape(path);
        }

        var name = fi.Name.Length > 50 ? fi.Name[..47] + "..." : fi.Name;
        var size = FormatSize(fi.Length);
        var age  = FormatAge(DateTime.UtcNow - fi.LastWriteTimeUtc);

        return Theme.MenuRow("▸", name, $"{size,-10} {age}");
    }

    private static List<FileInfo> ListBackups()
    {
        try
        {
            var dir = Path.Combine(AppContext.BaseDirectory, "Backups");
            if (!Directory.Exists(dir))
            {
                return new List<FileInfo>();
            }
            return new DirectoryInfo(dir)
                .EnumerateFiles("*.bak", SearchOption.TopDirectoryOnly)
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .ToList();
        }
        catch
        {
            return new List<FileInfo>();
        }
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024                 => $"{bytes} B",
        < 1024L * 1024         => $"{bytes / 1024.0:0.0} KB",
        < 1024L * 1024 * 1024  => $"{bytes / (1024.0 * 1024):0.0} MB",
        _                      => $"{bytes / (1024.0 * 1024 * 1024):0.0} GB"
    };

    private static string FormatAge(TimeSpan age) => age.TotalMinutes switch
    {
        < 1                => "just now",
        < 60               => $"{(int)age.TotalMinutes} min ago",
        < 60 * 24          => $"{(int)age.TotalHours} h ago",
        _                  => $"{(int)age.TotalDays} d ago"
    };

    private static bool TryEnsureDirectory(string? dir)
    {
        if (string.IsNullOrWhiteSpace(dir))
        {
            return true;
        }
        try
        {
            Directory.CreateDirectory(dir);
            return true;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[{Theme.Danger}]Cannot create directory {dir}: {Theme.Escape(ex.Message)}[/]");
            return false;
        }
    }

    private const string TdbManage  = "manage";
    private const string TdbRun     = "run";
    private const string TdbBack    = "back";
    private const string TdbAdd     = "add";
    private const string TdbRemove  = "remove";

    private static string MenuLine(string glyph, string label, string description) =>
        Theme.MenuRow(glyph, label, description);

    private void RunTestDbScriptsFlow()
    {
        Banner.Section("Test DB scripts", showBackHint: true);

        while (true)
        {
            try
            {
                var registry = TestDatabaseRegistry.Load();

                var prompt = new SelectionPrompt<string>
                {
                    HighlightStyle = Theme.HighlightStyle,
                    PageSize = 5
                }
                .Title($"[bold {Theme.Title}]Test DB scripts[/]  [{Theme.Muted}]({registry.Entries.Count} registered)[/]")
                .UseConverter(c => c switch
                {
                    TdbManage => MenuLine("✱", "Manage test databases",   "Add or remove DBs from the registry"),
                    TdbRun    => MenuLine("▸", "Run scripts on test DBs", "Run every .sql in TestDbScripts on selected DBs"),
                    TdbBack   => MenuLine("←", "Back to main menu",       "Return to the main menu"),
                    _ => c
                })
                .AddChoices(TdbManage, TdbRun, TdbBack);

                var choice = AnsiConsole.Prompt(prompt);
                if (choice == TdbBack)
                {
                    return;
                }

                if (choice == TdbManage)
                {
                    ManageTestDbsFlow(registry);
                }
                else if (choice == TdbRun)
                {
                    RunScriptsOnTestDbsFlow(registry);
                }
            }
            catch (ReturnToMenuException)
            {
                AnsiConsole.MarkupLine($"[{Theme.Muted}]back[/]");
            }
        }
    }

    private void ManageTestDbsFlow(TestDatabaseRegistry registry)
    {
        Banner.Section("Manage test databases", showBackHint: true);

        while (true)
        {
            try
            {

            if (registry.Entries.Count == 0)
            {
                AnsiConsole.MarkupLine($"[{Theme.Muted}]No test databases registered yet.[/]");
            }
            else
            {
                var table = new Table
                {
                    Border = TableBorder.Horizontal,
                    BorderStyle = Theme.BorderStyle,
                    ShowRowSeparators = false,
                    Expand = false
                };
                table.AddColumn(new TableColumn($"[{Theme.Subtitle}]Database[/]").NoWrap());
                table.AddColumn(new TableColumn($"[{Theme.Subtitle}]Owner[/]").NoWrap());
                foreach (var e in registry.Entries)
                {
                    table.AddRow(
                        $"[white]{Theme.Escape(e.Name)}[/]",
                        $"[{Theme.Muted}]{Theme.Escape(e.Owner)}[/]");
                }
                AnsiConsole.Write(table);
            }

            AnsiConsole.WriteLine();

            var choices = new List<string> { TdbAdd };
            if (registry.Entries.Count > 0)
            {
                choices.Add(TdbRemove);
            }
            choices.Add(TdbBack);

            var prompt = new SelectionPrompt<string>
            {
                HighlightStyle = Theme.HighlightStyle,
                PageSize = 5
            }
            .Title($"[bold {Theme.Title}]Manage test databases[/]")
            .UseConverter(c => c switch
            {
                TdbAdd    => MenuLine("+", "Add a database",    "Register a new test DB (validated on server)"),
                TdbRemove => MenuLine("−", "Remove a database", "Unregister a DB from the list"),
                TdbBack   => MenuLine("←", "Back",              "Return to the previous menu"),
                _ => c
            })
            .AddChoices(choices);

            var choice = AnsiConsole.Prompt(prompt);
            if (choice == TdbBack)
            {
                return;
            }

            if (choice == TdbAdd)
            {
                AddTestDb(registry);
            }
            else if (choice == TdbRemove)
            {
                RemoveTestDb(registry);
            }
            }
            catch (ReturnToMenuException)
            {
                AnsiConsole.MarkupLine($"[{Theme.Muted}]back[/]");
            }
        }
    }

    private void AddTestDb(TestDatabaseRegistry registry)
    {
        var name = Prompts.AskValidated(
            "Database name",
            null,
            value =>
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    return "Database name is required.";
                }
                if (!SafeDbIdentifier.IsMatch(value))
                {
                    return "Database name must start with a letter or underscore and contain only letters, digits, or underscores.";
                }
                if (registry.Contains(value))
                {
                    return $"'{value}' is already in the registry.";
                }
                if (!_actions.DatabaseExists(value))
                {
                    return $"Database '{value}' was not found on {_actions.Server}.";
                }
                return null;
            });

        var owner = Prompts.AskRequired("Owner (e.g. Kishor, Leo)");

        var entry = new TestDatabase { Name = name, Owner = owner };
        if (registry.Add(entry))
        {
            AnsiConsole.MarkupLine($"[{Theme.Success}]Added {Theme.Escape(name)} ({Theme.Escape(owner)}).[/]");
        }
        else
        {
            AnsiConsole.MarkupLine($"[{Theme.Warning}]Could not add (already registered).[/]");
        }
    }

    private static void RemoveTestDb(TestDatabaseRegistry registry)
    {
        if (registry.Entries.Count == 0)
        {
            return;
        }

        const string CancelSentinel = "__cancel__";
        var choices = new List<string>(registry.Entries.Select(e => e.Name)) { CancelSentinel };

        var prompt = new SelectionPrompt<string>
        {
            HighlightStyle = Theme.HighlightStyle,
            PageSize = 15
        }
        .Title($"[bold {Theme.Title}]Pick a database to remove[/]")
        .UseConverter(c =>
        {
            if (c == CancelSentinel)
            {
                return MenuLine("←", "Cancel", "Don't remove anything");
            }
            var entry = registry.Entries.First(e => e.Name == c);
            return MenuLine("▹", entry.Name, entry.Owner);
        })
        .AddChoices(choices);

        var picked = AnsiConsole.Prompt(prompt);
        if (picked == CancelSentinel)
        {
            return;
        }

        if (!Prompts.AskYesNo($"Remove {picked} from the registry?", true))
        {
            return;
        }

        if (registry.Remove(picked))
        {
            AnsiConsole.MarkupLine($"[{Theme.Success}]Removed {Theme.Escape(picked)}.[/]");
        }
    }

    private void RunScriptsOnTestDbsFlow(TestDatabaseRegistry registry)
    {
        Banner.Section("Run scripts on test DBs", showBackHint: true);

        if (registry.Entries.Count == 0)
        {
            AnsiConsole.MarkupLine($"[{Theme.Warning}]No test databases registered. Add at least one via Manage.[/]");
            return;
        }

        if (!Directory.Exists(AppPaths.TestDbScriptsDir))
        {
            AnsiConsole.MarkupLine($"[{Theme.Warning}]Scripts folder not found: {Theme.Escape(AppPaths.TestDbScriptsDir)}[/]");
            AnsiConsole.MarkupLine($"[{Theme.Muted}]Drop one or more .sql files into that folder and try again.[/]");
            return;
        }

        var scriptCount = Directory.EnumerateFiles(AppPaths.TestDbScriptsDir, "*.sql", SearchOption.TopDirectoryOnly).Count();
        if (scriptCount == 0)
        {
            AnsiConsole.MarkupLine($"[{Theme.Warning}]No .sql files in {Theme.Escape(AppPaths.TestDbScriptsDir)}.[/]");
            return;
        }

        var multi = new MultiSelectionPrompt<string>
        {
            HighlightStyle = Theme.HighlightStyle,
            PageSize = 15,
            Required = false
        }
        .Title($"[{Theme.Title}]Select databases to run on[/]  [{Theme.Muted}](space toggles, enter confirms)[/]")
        .InstructionsText($"[{Theme.Muted}]All pre-selected. Space to uncheck. Enter to continue. Uncheck all + Enter to cancel.[/]")
        .UseConverter(name =>
        {
            var entry = registry.Entries.First(e => e.Name == name);
            return $"{Markup.Escape(entry.Name).PadRight(45)}[{Theme.Subtitle}]{Markup.Escape(entry.Owner)}[/]";
        });

        foreach (var e in registry.Entries)
        {
            multi.AddChoice(e.Name);
        }
        foreach (var e in registry.Entries)
        {
            multi.Select(e.Name);
        }

        var picked = AnsiConsole.Prompt(multi);
        if (picked.Count == 0)
        {
            AnsiConsole.MarkupLine($"[{Theme.Muted}]No databases selected. Cancelled.[/]");
            return;
        }

        if (!Prompts.AskYesNo($"Run {scriptCount} script{(scriptCount == 1 ? "" : "s")} on {picked.Count} database{(picked.Count == 1 ? "" : "s")}?", true))
        {
            AnsiConsole.MarkupLine($"[{Theme.Muted}]Cancelled.[/]");
            return;
        }

        var ordered = registry.Entries.Where(e => picked.Contains(e.Name)).ToList();
        _actions.RunTestDbScripts(ordered);
    }
}
