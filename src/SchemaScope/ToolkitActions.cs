using System.Diagnostics;
using System.IO;
using SchemaScope.Auditing;
using SchemaScope.Parsing;
using SchemaScope.Sql;
using SchemaScope.TestDb;
using SchemaScope.Ui;
using SchemaScope.Verification;
using Spectre.Console;

namespace SchemaScope;

public sealed class ToolkitActions
{
    private readonly SqlConnectionFactory _factory;
    private readonly VersionScriptLocator _locator;
    private readonly string _prepatchFile;
    private readonly string _defaultSchema;

    public ToolkitActions(
        SqlConnectionFactory factory,
        VersionScriptLocator locator,
        string prepatchFile,
        string defaultSchema = "dbo")
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _locator = locator ?? throw new ArgumentNullException(nameof(locator));
        _prepatchFile = prepatchFile ?? string.Empty;
        _defaultSchema = string.IsNullOrWhiteSpace(defaultSchema) ? "dbo" : defaultSchema;
    }

    public string Server => _factory.Server;

    public string? TryGetServerDefaultBackupPath()
    {
        using var logger = RunLogger.Create("server");
        return new DatabaseOperations(_factory, logger).TryGetServerDefaultBackupPath();
    }

    public string? TryGetBackupSourceDatabase(string backupPath)
    {
        using var logger = RunLogger.Create("server");
        return new DatabaseOperations(_factory, logger).TryGetBackupSourceDatabase(backupPath);
    }

    public bool RunBackup(string sourceDb, string backupPath)
    {
        using var logger = RunLogger.Create(sourceDb);
        Banner.ConnectionInfo(_factory.Server, sourceDb, _locator.FolderPath, logger.LogFilePath);
        logger.Info($"Started: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");

        var dbOps = new DatabaseOperations(_factory, logger);
        var summary = new List<string>();
        var ok = false;

        RenderWithProgress($"Backup {sourceDb}", task =>
        {
            ok = dbOps.Backup(
                sourceDb,
                backupPath,
                onProgress: pct => task.Value = pct,
                onSummary:  msg => summary.Add(msg));
        });

        if (ok)
        {
            RenderOperationCard("Backup complete", backupPath, summary);
        }
        logger.Blank();
        logger.Info($"Done. Full log: {logger.LogFilePath}");
        return ok;
    }

    public bool RunRestore(string targetDb, string backupPath, string? dataDir, string? logDir)
    {
        using var logger = RunLogger.Create(targetDb);
        Banner.ConnectionInfo(_factory.Server, targetDb, _locator.FolderPath, logger.LogFilePath);
        logger.Info($"Started: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");

        var dbOps = new DatabaseOperations(_factory, logger);
        var summary = new List<string>();
        var ok = false;

        RenderWithProgress($"Restore {targetDb}", task =>
        {
            ok = dbOps.Restore(
                targetDb,
                backupPath,
                dataDir,
                logDir,
                onProgress: pct => task.Value = pct,
                onSummary:  msg => summary.Add(msg));
        });

        if (ok)
        {
            RenderOperationCard("Restore complete", backupPath, summary, headlineDetail: $"into {targetDb}");
        }
        logger.Blank();
        logger.Info($"Done. Full log: {logger.LogFilePath}");
        return ok;
    }

    public bool RunClone(string sourceDb, string targetDb, string? backupPath, string? dataDir, string? logDir)
    {
        using var logger = RunLogger.Create($"{sourceDb}_to_{targetDb}");
        Banner.ConnectionInfo(_factory.Server, $"{sourceDb} -> {targetDb}", _locator.FolderPath, logger.LogFilePath);
        logger.Info($"Started: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");

        var dbOps = new DatabaseOperations(_factory, logger);
        var summary = new List<string>();
        var ok = false;

        AnsiConsole.Progress()
            .AutoClear(false)
            .HideCompleted(false)
            .Columns(
                new TaskDescriptionColumn(),
                new ProgressBarColumn { CompletedStyle = Theme.HighlightStyle, FinishedStyle = Theme.HighlightStyle },
                new PercentageColumn(),
                new SpinnerColumn(Spinner.Known.Dots))
            .Start(ctx =>
            {
                var backup  = ctx.AddTask($"[{Theme.Info}]Backup  {Markup.Escape(sourceDb)}[/]", maxValue: 100);
                var restore = ctx.AddTask($"[{Theme.Info}]Restore {Markup.Escape(targetDb)}[/]", maxValue: 100);
                ok = dbOps.Clone(
                    sourceDb,
                    targetDb,
                    backupPath,
                    dataDir,
                    logDir,
                    onBackupProgress:  pct => backup.Value = pct,
                    onRestoreProgress: pct => restore.Value = pct,
                    onSummary:         msg => summary.Add(msg));
                if (ok)
                {
                    backup.Value = 100;
                    restore.Value = 100;
                }
            });

        if (ok && !string.IsNullOrWhiteSpace(backupPath))
        {
            RenderOperationCard("Clone complete", backupPath, summary, headlineDetail: $"{sourceDb} to {targetDb}");
        }
        logger.Blank();
        logger.Info($"Done. Full log: {logger.LogFilePath}");
        return ok;
    }

    private void RunWithSession(string database, Action<SqlSession, RunLogger, SqlScriptRunner> body)
    {
        using var logger = RunLogger.Create(database);
        Banner.ConnectionInfo(_factory.Server, database, _locator.FolderPath, logger.LogFilePath);
        logger.Info($"Started: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");

        SqlSession session;
        try
        {
            session = SqlSession.Open(_factory, database);
        }
        catch (Exception ex)
        {
            logger.Error($"Could not connect to {_factory.Server} / {database}: {ex.Message}");
            return;
        }

        try
        {
            var runner = new SqlScriptRunner(session, logger, database);
            body(session, logger, runner);
        }
        finally
        {
            session.Dispose();
            logger.Blank();
            logger.Info($"Done. Full log: {logger.LogFilePath}");
        }
    }

    private static void RenderWithProgress(string description, Action<ProgressTask> body)
    {
        AnsiConsole.Progress()
            .AutoClear(false)
            .HideCompleted(false)
            .Columns(
                new TaskDescriptionColumn(),
                new ProgressBarColumn { CompletedStyle = Theme.HighlightStyle, FinishedStyle = Theme.HighlightStyle },
                new PercentageColumn(),
                new SpinnerColumn(Spinner.Known.Dots))
            .Start(ctx =>
            {
                var task = ctx.AddTask($"[{Theme.Info}]{Markup.Escape(description)}[/]", maxValue: 100);
                body(task);
                if (task.Value < 100)
                {
                    task.Value = 100;
                }
            });
    }

    private static void RenderOperationCard(string headline, string path, IReadOnlyList<string> summaryLines, string? headlineDetail = null)
    {
        var folder = Path.GetDirectoryName(path) ?? string.Empty;
        var file   = Path.GetFileName(path);

        string? folderLink = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder))
            {
                folderLink = new Uri(folder).AbsoluteUri;
            }
        }
        catch
        {
            folderLink = null;
        }

        var stats = summaryLines
            .Where(s => s.Contains("successfully processed", StringComparison.OrdinalIgnoreCase)
                     || s.Contains("MB/sec", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var copied = TryCopyToClipboard(path);

        var body = new System.Text.StringBuilder();
        body.Append($"[bold {Theme.Success}]{Markup.Escape(headline)}[/]");
        if (!string.IsNullOrWhiteSpace(headlineDetail))
        {
            body.Append($"  [{Theme.Muted}]{Markup.Escape(headlineDetail)}[/]");
        }
        body.AppendLine();

        var pathMarkup = folderLink is not null
            ? $"[underline link={folderLink}]{Markup.Escape(path)}[/]"
            : $"[white]{Markup.Escape(path)}[/]";
        body.AppendLine(pathMarkup);

        var hints = new List<string>();
        if (copied)
        {
            hints.Add("path copied to clipboard");
        }
        if (folderLink is not null)
        {
            hints.Add("Ctrl+Click path to open folder");
        }
        if (hints.Count > 0)
        {
            body.AppendLine($"[{Theme.Muted}]{string.Join(" · ", hints)}[/]");
        }

        foreach (var s in stats)
        {
            body.AppendLine($"[{Theme.Muted}]{Markup.Escape(s)}[/]");
        }

        AnsiConsole.WriteLine();
        var panel = new Panel(new Markup(body.ToString().TrimEnd()))
        {
            Border = BoxBorder.Rounded,
            BorderStyle = Theme.BorderStyle,
            Padding = new Padding(2, 0, 2, 0),
            Header = new PanelHeader($"[{Theme.Success}] done [/]", Justify.Left),
            Expand = false
        };
        AnsiConsole.Write(panel);
        _ = file;
    }

    // OSC 52 clipboard-write: supported by Windows Terminal, iTerm2, Kitty, Alacritty.
    // Silently no-op on terminals that ignore the escape.
    private static bool TryCopyToClipboard(string text)
    {
        try
        {
            if (Console.IsOutputRedirected)
            {
                return false;
            }
            var base64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(text));
            // ESC ] 52 ; c ; <base64> BEL
            Console.Write($"]52;c;{base64}");
            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool RunPrepatch(string database)
    {
        if (string.IsNullOrWhiteSpace(_prepatchFile) || !File.Exists(_prepatchFile))
        {
            AnsiConsole.MarkupLine($"[{Theme.Danger}]Prepatch file not found: {Theme.Escape(_prepatchFile)}[/]");
            return false;
        }

        var ok = false;
        RunWithSession(database, (_, _, runner) =>
        {
            ok = runner.Execute("Prepatch (missing tables / seed data)", _prepatchFile);
        });
        return ok;
    }

    public bool RunPrepatchAndVersionRange(string database, int from, int to)
    {
        if (string.IsNullOrWhiteSpace(_prepatchFile) || !File.Exists(_prepatchFile))
        {
            AnsiConsole.MarkupLine($"[{Theme.Danger}]Prepatch file not found: {Theme.Escape(_prepatchFile)}[/]");
            return false;
        }

        var ok = false;
        RunWithSession(database, (_, logger, runner) =>
        {
            if (!runner.Execute("Prepatch (missing tables / seed data)", _prepatchFile))
            {
                logger.Error("Aborting. Fix prepatch failure before running version scripts.");
                return;
            }
            RunVersionRangeInner(logger, runner, from, to);
            ok = true;
        });
        return ok;
    }

    public void RunVersionRange(string database, int from, int to)
    {
        RunWithSession(database, (_, logger, runner) => RunVersionRangeInner(logger, runner, from, to));
    }

    private void RunVersionRangeInner(RunLogger logger, SqlScriptRunner runner, int from, int to)
    {
        if (from > 0 && to > 0 && from > to)
        {
            logger.Warn($"Start ({from}) is greater than End ({to}). No scripts to run.");
            return;
        }

        var scripts = _locator.GetInRange(from, to);
        if (scripts.Count == 0)
        {
            logger.Warn($"No version scripts found in range {from} to {to}.");
            return;
        }

        int okCount = 0;
        int failCount = 0;

        foreach (var s in scripts)
        {
            var ok = runner.Execute(s.Label, s.File.FullName);
            if (ok)
            {
                okCount++;
            }
            else
            {
                failCount++;
            }
        }

        logger.Blank();
        logger.Info($"Summary: {okCount} OK, {failCount} FAIL");
    }

    public void RunSpecificVersion(string database, int number)
    {
        if (number <= 0)
        {
            AnsiConsole.MarkupLine($"[{Theme.Warning}]No version entered.[/]");
            return;
        }

        var script = _locator.GetSingle(number);
        if (script is null)
        {
            AnsiConsole.MarkupLine($"[{Theme.Danger}]No script named {_locator.Label(number)} in {Theme.Escape(_locator.FolderPath)}[/]");
            return;
        }

        RunWithSession(database, (_, _, runner) => runner.Execute(script.Label, script.File.FullName));
    }

    public void DetectCurrentVersion(string database, int startFrom = 0)
    {
        RunWithSession(database, (session, logger, _) =>
        {
            var report = RunAuditWithProgress(session, logger, startFrom, 0);
            if (report is null)
            {
                logger.Warn(startFrom > 0
                    ? $"No version scripts found at or above {_locator.Label(startFrom)}."
                    : $"No version scripts found in {_locator.FolderPath}.");
                return;
            }

            AnsiConsole.WriteLine();
            RenderAuditSummary(report);
            AnsiConsole.WriteLine();
            RenderAuditVerdict(logger, report);
        });
    }

    public int RunAudit(string database, int from, int to, bool json, string? reportPath, bool strict, string? journal = null)
    {
        using var logger = RunLogger.Create(database);
        logger.LogOnly($"Audit started: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");

        if (!json)
        {
            Banner.ConnectionInfo(_factory.Server, database, _locator.FolderPath, logger.LogFilePath);
        }

        SqlSession session;
        try
        {
            session = SqlSession.Open(_factory, database);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Cannot connect to {_factory.Server} / {database}: {ex.Message}");
            logger.LogOnly($"Cannot connect: {ex.Message}");
            return 3;
        }

        AuditReport? report;
        using (session)
        {
            report = json
                ? new SchemaAuditor(session, _locator, _defaultSchema)
                    .Run(from, to, (script, entry) => LogAuditEntry(logger, script, entry))
                : RunAuditWithProgress(session, logger, from, to);

            if (report is not null && journal is not null)
            {
                var provider = journal.Equals("auto", StringComparison.OrdinalIgnoreCase)
                    ? JournalReader.DetectProvider(session)
                    : journal.ToLowerInvariant();

                if (provider is null)
                {
                    Console.Error.WriteLine(
                        $"No known journal table found in {database} (looked for {JournalReader.DbUpTable} and {JournalReader.FlywayTable}).");
                    return 2;
                }

                var read = JournalReader.Read(session, provider);
                if (read is null)
                {
                    Console.Error.WriteLine(
                        $"Journal table for provider '{provider}' not found in {database}.");
                    return 2;
                }

                var (table, entries) = read.Value;
                report = report with { Journal = JournalReader.CrossCheck(provider, table, entries, report) };
                logger.LogOnly($"Journal: {provider} ({table}), {entries.Count} entries");
            }
        }

        if (report is null)
        {
            Console.Error.WriteLine(from > 0
                ? $"No version scripts found at or above {_locator.Label(from)}."
                : $"No version scripts found in {_locator.FolderPath}.");
            return 2;
        }

        if (!string.IsNullOrWhiteSpace(reportPath))
        {
            try
            {
                File.WriteAllText(reportPath, report.ToJson());
                logger.LogOnly($"Report written: {reportPath}");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine($"Could not write report to {reportPath}: {ex.Message}");
                return 2;
            }
        }

        if (json)
        {
            Console.WriteLine(report.ToJson());
        }
        else
        {
            AnsiConsole.WriteLine();
            RenderAuditSummary(report);
            RenderAuditMismatchDetails(report);
            RenderJournalAudit(report.Journal);
            AnsiConsole.WriteLine();
            RenderAuditVerdict(logger, report);
            if (!string.IsNullOrWhiteSpace(reportPath))
            {
                AnsiConsole.MarkupLine($"[{Theme.Muted}]JSON report: {Theme.Escape(reportPath!)}[/]");
            }
            logger.Blank();
            logger.Info($"Done. Full log: {logger.LogFilePath}");
        }

        return report.ToExitCode(strict);
    }

    private AuditReport? RunAuditWithProgress(SqlSession session, RunLogger logger, int startFrom, int endAt)
    {
        var from = startFrom <= 0 ? 0 : startFrom;
        var scripts = _locator.GetInRange(from, endAt);
        if (scripts.Count == 0)
        {
            return null;
        }

        logger.Info($"Probing {scripts.Count} version scripts from {scripts[0].Label} to {scripts[^1].Label} ...");
        AnsiConsole.WriteLine();

        AuditReport? report = null;
        AnsiConsole.Progress()
            .AutoClear(false)
            .HideCompleted(false)
            .Columns(
                new TaskDescriptionColumn(),
                new ProgressBarColumn { CompletedStyle = Theme.HighlightStyle, FinishedStyle = Theme.HighlightStyle },
                new PercentageColumn(),
                new SpinnerColumn(Spinner.Known.Dots))
            .Start(ctx =>
            {
                var task = ctx.AddTask($"[{Theme.Info}]Probing[/]", maxValue: scripts.Count);
                report = new SchemaAuditor(session, _locator, _defaultSchema).Run(from, endAt, (script, entry) =>
                {
                    LogAuditEntry(logger, script, entry);
                    task.Description = $"[{Theme.Info}]Probing[/]  [{Theme.Muted}]{script.Label}[/]";
                    task.Increment(1);
                });
            });

        return report;
    }

    private static void LogAuditEntry(RunLogger logger, VersionScript script, AuditScriptResult entry)
    {
        logger.LogOnly(entry.Status == AuditScriptStatus.NoDdl
            ? $"  {script.Label} no-ddl"
            : $"  {script.Label} {entry.Status.ToString().ToLowerInvariant()} {entry.ObjectsPresent}/{entry.ObjectsTotal}");
        foreach (var mismatch in entry.Mismatches)
        {
            logger.LogOnly($"    {mismatch.Status.ToLowerInvariant()} {mismatch.Kind} {mismatch.Name}: {mismatch.Detail}");
        }
    }

    private static void RenderAuditSummary(AuditReport report)
    {
        var appliedTotal = report.Totals.Applied + report.Totals.NoDdl;
        var summary = new Grid().AddColumn(new GridColumn().PadRight(3)).AddColumn();
        summary.AddRow($"[{Theme.Subtitle}]Applied[/]", $"[{Theme.Success}]{appliedTotal}[/]");
        summary.AddRow($"[{Theme.Subtitle}]Partial[/]", $"[{Theme.Warning}]{report.Totals.Partial}[/]  [{Theme.Muted}]({report.Drift.Count(d => d.Status == AuditScriptStatus.Partial)} before head, {report.Pending.Count(d => d.Status == AuditScriptStatus.Partial)} after)[/]");
        summary.AddRow($"[{Theme.Subtitle}]Missing[/]", $"[{Theme.Danger}]{report.Totals.Missing}[/]  [{Theme.Muted}]({report.Drift.Count(d => d.Status == AuditScriptStatus.Missing)} before head, {report.Pending.Count(d => d.Status == AuditScriptStatus.Missing)} after)[/]");
        AnsiConsole.Write(summary);

        if (report.Pending.Count > 0)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"[{Theme.Subtitle}]Unapplied scripts after current version:[/]");
            RenderAuditEntryList(report.Pending.Take(15).ToList(), report.Pending.Count);
        }

        if (report.Drift.Count > 0)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"[{Theme.Subtitle}]Drift (older scripts whose objects were later dropped or renamed):[/]");
            RenderAuditEntryList(report.Drift.Take(15).ToList(), report.Drift.Count);
        }

        if (report.Totals.WithParseErrors > 0)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine(
                $"[{Theme.Warning}]{report.Totals.WithParseErrors} script{(report.Totals.WithParseErrors == 1 ? "" : "s")} had T-SQL parse errors; detected objects may be incomplete. See the log file.[/]");
        }
    }

    private static void RenderAuditEntryList(IReadOnlyList<AuditScriptResult> entries, int totalCount)
    {
        var table = new Table
        {
            Border = TableBorder.Horizontal,
            BorderStyle = Theme.BorderStyle,
            ShowRowSeparators = false,
            Expand = false
        };
        table.AddColumn(new TableColumn($"[{Theme.Subtitle}]Version[/]").NoWrap());
        table.AddColumn(new TableColumn($"[{Theme.Subtitle}]Status[/]").NoWrap());
        table.AddColumn(new TableColumn($"[{Theme.Subtitle}]Objects[/]").NoWrap());

        foreach (var e in entries)
        {
            var status = e.Status == AuditScriptStatus.Partial
                ? $"[{Theme.Warning}]partial[/]"
                : $"[{Theme.Danger}]missing[/]";
            table.AddRow(e.Label, status, $"{e.ObjectsPresent}/{e.ObjectsTotal}");
        }
        AnsiConsole.Write(table);
        if (entries.Count < totalCount)
        {
            AnsiConsole.MarkupLine($"[{Theme.Muted}]... {totalCount - entries.Count} more (see log file)[/]");
        }
    }

    private static void RenderAuditMismatchDetails(AuditReport report)
    {
        var interesting = report.Pending.Concat(report.Drift)
            .Where(e => e.Mismatches.Count > 0)
            .Take(10)
            .ToList();

        if (interesting.Count == 0)
        {
            return;
        }

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[{Theme.Subtitle}]Object-level detail:[/]");

        var table = new Table
        {
            Border = TableBorder.Horizontal,
            BorderStyle = Theme.BorderStyle,
            ShowRowSeparators = false,
            Expand = true
        };
        table.AddColumn(new TableColumn($"[{Theme.Subtitle}]Version[/]").NoWrap());
        table.AddColumn(new TableColumn(string.Empty).NoWrap().Width(12).Padding(0, 1));
        table.AddColumn(new TableColumn($"[{Theme.Subtitle}]Kind[/]").NoWrap());
        table.AddColumn(new TableColumn($"[{Theme.Subtitle}]Object[/]").NoWrap());
        table.AddColumn(new TableColumn($"[{Theme.Subtitle}]Detail[/]"));

        foreach (var entry in interesting)
        {
            foreach (var m in entry.Mismatches.Take(8))
            {
                var pill = m.Status == "Differs"
                    ? $"[{Theme.PillDiffers}] DIFFERS [/]"
                    : $"[{Theme.PillMissing}] MISSING [/]";
                var name = m.Schema is null ? m.Name : $"{m.Schema}.{(m.Parent is null ? "" : m.Parent + ".")}{m.Name}";
                table.AddRow(
                    entry.Label,
                    pill,
                    $"[{Theme.Muted}]{Theme.Escape(m.Kind)}[/]",
                    $"[white]{Theme.Escape(name)}[/]",
                    $"[{Theme.Muted}]{Theme.Escape(m.Detail)}[/]");
            }
            if (entry.Mismatches.Count > 8)
            {
                table.AddRow(entry.Label, string.Empty, string.Empty,
                    $"[{Theme.Muted}]... {entry.Mismatches.Count - 8} more objects[/]", string.Empty);
            }
        }

        AnsiConsole.Write(table);
    }

    private static void RenderJournalAudit(JournalAudit? journal)
    {
        if (journal is null)
        {
            return;
        }

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine(
            $"[{Theme.Subtitle}]Journal cross-check[/]  [{Theme.Muted}]{Theme.Escape(journal.Provider)} · {Theme.Escape(journal.Table)} · {journal.Entries} entries[/]");

        if (journal.IsClean)
        {
            AnsiConsole.MarkupLine($"  [{Theme.Success}]✓[/] The journal agrees with the schema evidence.");
            return;
        }

        RenderFindings(journal.JournaledButNotApplied, "JOURNAL LIES", Theme.PillMissing,
            "recorded as applied, but the schema disagrees");
        RenderFindings(journal.FailedJournalEntries, "FAILED", Theme.PillDiffers,
            "recorded as failed migrations");
        RenderFindings(journal.AppliedButNotJournaled, "UNTRACKED", Theme.PillDiffers,
            "present in the schema, missing from the journal");
        RenderFindings(journal.UnmatchedJournalEntries, "ORPHANED", Theme.PillNeutral,
            "journal entries with no matching local script");

        static void RenderFindings(IReadOnlyList<JournalFinding> findings, string pillText, string pillStyle, string caption)
        {
            if (findings.Count == 0)
            {
                return;
            }

            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"  [{pillStyle}] {pillText} [/] [{Theme.Muted}]{findings.Count} — {caption}[/]");
            foreach (var f in findings.Take(10))
            {
                var label = f.Label is null ? string.Empty : $"[{Theme.Accent}]{Theme.Escape(f.Label)}[/]  ";
                AnsiConsole.MarkupLine($"    {label}[white]{Theme.Escape(f.ScriptName)}[/]");
                AnsiConsole.MarkupLine($"      [{Theme.Muted}]{Theme.Escape(f.Detail)}[/]");
            }
            if (findings.Count > 10)
            {
                AnsiConsole.MarkupLine($"    [{Theme.Muted}]... {findings.Count - 10} more (see JSON report)[/]");
            }
        }
    }

    private void RenderAuditVerdict(RunLogger logger, AuditReport report)
    {
        var body = new System.Text.StringBuilder();

        switch (report.Verdict)
        {
            case AuditVerdict.NoAppliedVersionInRange:
                body.AppendLine($"[bold {Theme.Warning}]NO APPLIED VERSION IN RANGE[/]");
                body.AppendLine($"[{Theme.Muted}]No fully-applied script found in range {_locator.Label(report.StartFrom)} to {_locator.Label(report.Head)}. DB may be below {_locator.Label(report.StartFrom)}; rerun from 1 for a full scan.[/]");
                break;

            case AuditVerdict.NotInitialised:
                body.AppendLine($"[bold {Theme.Danger}]DATABASE NOT INITIALISED[/]");
                body.AppendLine($"[{Theme.Muted}]No version script has been fully applied to this database.[/]");
                break;

            case AuditVerdict.AtHead:
                body.AppendLine($"[bold {Theme.Success}]AT HEAD[/]");
                body.AppendLine($"[{Theme.Muted}]Database is at version {report.CurrentVersionLabel} (head).[/]");
                break;

            default:
                body.AppendLine($"[bold {Theme.Warning}]BEHIND HEAD[/]");
                body.AppendLine($"[{Theme.Muted}]Applied through {report.CurrentVersionLabel}. Head is {_locator.Label(report.Head)}. {report.Pending.Count} script{(report.Pending.Count == 1 ? "" : "s")} pending.[/]");
                break;
        }

        if (report.PartialScan && report.CurrentVersion > 0)
        {
            body.AppendLine($"[{Theme.Muted}]Partial scan: started at {_locator.Label(report.StartFrom)}. Versions below that were not checked.[/]");
        }

        if (report.Drift.Count > 0)
        {
            body.AppendLine($"[{Theme.Warning}]{report.Drift.Count} earlier script{(report.Drift.Count == 1 ? "" : "s")} show drift (objects dropped or renamed by later migrations). Informational only - not a blocker.[/]");
        }

        var panel = new Panel(new Markup(body.ToString().TrimEnd()))
        {
            Border = BoxBorder.Rounded,
            BorderStyle = Theme.BorderStyle,
            Padding = new Padding(2, 0, 2, 0),
            Header = new PanelHeader($"[{Theme.Brand}] verdict [/]", Justify.Left),
            Expand = false
        };
        AnsiConsole.Write(panel);

        logger.LogOnly(report.Verdict switch
        {
            AuditVerdict.NotInitialised => "Audit: DATABASE NOT INITIALISED",
            AuditVerdict.NoAppliedVersionInRange => "Audit: NO APPLIED VERSION IN RANGE",
            AuditVerdict.AtHead => $"Audit: AT HEAD ({report.CurrentVersionLabel})",
            _ => $"Audit: {report.CurrentVersionLabel} applied, head {_locator.Label(report.Head)}, {report.Pending.Count} pending, {report.Drift.Count} drift"
        });
    }

    public void VerifyVersion(string database, int number)
    {
        if (number <= 0)
        {
            AnsiConsole.MarkupLine($"[{Theme.Warning}]No version entered.[/]");
            return;
        }

        var script = _locator.GetSingle(number);
        if (script is null)
        {
            AnsiConsole.MarkupLine($"[{Theme.Danger}]Script {_locator.Label(number)} not found in {Theme.Escape(_locator.FolderPath)}[/]");
            return;
        }

        RunWithSession(database, (session, logger, _) =>
        {
            string raw;
            try
            {
                raw = File.ReadAllText(script.File.FullName);
            }
            catch (IOException ex)
            {
                logger.Error($"Could not read {script.File.FullName}: {ex.Message}");
                return;
            }

            var extraction = new DdlExtractor().Extract(raw);
            if (extraction.Objects.Count == 0)
            {
                logger.Warn($"No detectable DDL in {_locator.Label(number)} (script may be data-only).");
                foreach (var parseError in extraction.ParseErrors)
                {
                    logger.Muted($"parse: {parseError}");
                }
                return;
            }

            var verifier = new VersionVerifier(session, _defaultSchema);
            var report = verifier.Verify(number, extraction.Objects, extraction.ParseErrors);

            RenderVerificationReport(logger, report);
        });
    }

    public int VerifyForCi(string database, int number, bool json = false, string? reportPath = null)
    {
        if (number <= 0)
        {
            Console.Error.WriteLine("--verify requires a version number greater than 0.");
            return 2;
        }

        var script = _locator.GetSingle(number);
        if (script is null)
        {
            Console.Error.WriteLine($"Script {_locator.Label(number)} not found in {_locator.FolderPath}");
            return 2;
        }

        using var logger = RunLogger.Create(database);
        if (!json)
        {
            Banner.ConnectionInfo(_factory.Server, database, _locator.FolderPath, logger.LogFilePath);
        }

        SqlSession session;
        try
        {
            session = SqlSession.Open(_factory, database);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Cannot connect to {_factory.Server} / {database}: {ex.Message}");
            return 3;
        }

        string raw;
        VerificationReport report;
        using (session)
        {
            try
            {
                raw = File.ReadAllText(script.File.FullName);
            }
            catch (IOException ex)
            {
                Console.Error.WriteLine($"Could not read {script.File.FullName}: {ex.Message}");
                return 2;
            }

            var extraction = new DdlExtractor().Extract(raw);
            var verifier = new VersionVerifier(session, _defaultSchema);
            report = verifier.Verify(number, extraction.Objects, extraction.ParseErrors);
        }

        var mismatches = report.Results
            .Where(r => r.Status != VerificationStatus.Matches)
            .Select(r => new AuditObjectResult(
                r.Object.Kind.ToString(), r.Object.Schema, r.Object.Parent, r.Object.Name,
                r.Status.ToString(), r.Detail))
            .ToList();

        var verifyReport = new SingleVerifyReport(
            Tool: AppInfo.Name,
            ToolVersion: AppInfo.Version,
            ScannedAtUtc: DateTime.UtcNow,
            Server: _factory.Server,
            Database: database,
            Version: number,
            Label: _locator.Label(number),
            File: script.File.FullName,
            Verdict: report.Verdict.ToString(),
            ObjectsPresent: report.OkCount,
            ObjectsTotal: report.Results.Count,
            Differs: report.DiffersCount,
            Missing: report.MissingCount,
            ParseErrors: report.ParseWarnings.Count,
            Mismatches: mismatches);

        if (!string.IsNullOrWhiteSpace(reportPath))
        {
            try
            {
                File.WriteAllText(reportPath, verifyReport.ToJson());
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine($"Could not write report to {reportPath}: {ex.Message}");
                return 2;
            }
        }

        if (json)
        {
            Console.WriteLine(verifyReport.ToJson());
        }
        else
        {
            var line = report.Verdict == VerificationVerdict.NoObjects
                ? $"{_locator.Label(number)}: no detectable DDL (script may be data-only)"
                : $"{_locator.Label(number)}: {report.Verdict} (OK {report.OkCount}, differs {report.DiffersCount}, missing {report.MissingCount}, total {report.Results.Count})";
            var colour = report.Verdict is VerificationVerdict.FullyApplied or VerificationVerdict.NoObjects
                ? Theme.Success
                : Theme.Danger;
            AnsiConsole.MarkupLine($"[{colour}]{Theme.Escape(line)}[/]");
        }
        logger.LogOnly(BuildVerdictLogLine(report));

        return report.Verdict switch
        {
            VerificationVerdict.FullyApplied => 0,
            VerificationVerdict.NoObjects    => 0,
            _                                => 1
        };
    }

    private void RenderVerificationReport(RunLogger logger, VerificationReport report)
    {
        Banner.Section($"{_locator.Label(report.Version)} verification");

        if (report.ParseWarnings.Count > 0)
        {
            foreach (var warning in report.ParseWarnings)
            {
                logger.Muted($"parse: {warning}");
            }
            logger.Blank();
        }

        RenderSummaryStrip(report);
        AnsiConsole.WriteLine();

        var table = new Table
        {
            Border = TableBorder.Horizontal,
            BorderStyle = Theme.BorderStyle,
            ShowRowSeparators = false,
            Expand = true
        };
        table.AddColumn(new TableColumn(string.Empty).NoWrap().Width(12).Padding(0, 1));
        table.AddColumn(new TableColumn($"[{Theme.Subtitle}]Kind[/]").NoWrap());
        table.AddColumn(new TableColumn($"[{Theme.Subtitle}]Object[/]").NoWrap());
        table.AddColumn(new TableColumn($"[{Theme.Subtitle}]Detail[/]"));

        foreach (var r in report.Results)
        {
            table.AddRow(
                RenderStatusPill(r.Status),
                $"[{Theme.Muted}]{Theme.Escape(r.Object.Kind.ToString())}[/]",
                FormatObjectName(r.Object),
                $"[{Theme.Muted}]{Theme.Escape(r.Detail)}[/]");
        }

        AnsiConsole.Write(table);

        var withDumps = report.Results.Where(x => x.DbDumpPath is not null).ToList();
        if (withDumps.Count > 0)
        {
            AnsiConsole.WriteLine();
            foreach (var r in withDumps)
            {
                logger.Muted($"  {r.Object.Name}  db:   {r.DbDumpPath}");
                logger.Muted($"  {new string(' ', r.Object.Name.Length)}  file: {r.FileDumpPath}");
            }
        }

        AnsiConsole.WriteLine();
        RenderVerdictPanel(report);
        logger.LogOnly(BuildVerdictLogLine(report));
    }

    private string VersionLabel(int version) => _locator.Label(version);

    private static void RenderSummaryStrip(VerificationReport report)
    {
        var grid = new Grid()
            .AddColumn(new GridColumn().PadRight(4))
            .AddColumn(new GridColumn().PadRight(4))
            .AddColumn(new GridColumn().PadRight(4))
            .AddColumn(new GridColumn());

        grid.AddRow(
            Count($"[{Theme.Subtitle}]TOTAL[/]",   report.Results.Count, Theme.Muted),
            Count($"[{Theme.Success}]OK[/]",       report.OkCount,       Theme.Success),
            Count($"[{Theme.Warning}]DIFFERS[/]",  report.DiffersCount,  Theme.Warning),
            Count($"[{Theme.Danger}]MISSING[/]",   report.MissingCount,  Theme.Danger));

        AnsiConsole.Write(grid);

        static string Count(string label, int value, string numberColour) =>
            $"{label}  [bold {numberColour}]{value}[/]";
    }

    private static string RenderStatusPill(VerificationStatus status) => status switch
    {
        VerificationStatus.Matches => $"[{Theme.PillOk}] OK [/]",
        VerificationStatus.Differs => $"[{Theme.PillDiffers}] DIFFERS [/]",
        VerificationStatus.Missing => $"[{Theme.PillMissing}] MISSING [/]",
        _                           => $"[{Theme.PillNeutral}] ? [/]"
    };

    private static string FormatObjectName(Parsing.DdlObject obj)
    {
        var schemaPrefix = obj.Schema is null
            ? string.Empty
            : $"[{Theme.Muted}]{Theme.Escape(obj.Schema)}.[/]";

        if (obj.Parent is null)
        {
            return $"{schemaPrefix}[white]{Theme.Escape(obj.Name)}[/]";
        }
        return $"{schemaPrefix}[{Theme.Muted}]{Theme.Escape(obj.Parent)}.[/][white]{Theme.Escape(obj.Name)}[/]";
    }

    private void RenderVerdictPanel(VerificationReport report)
    {
        var (headline, subline, colour) = report.Verdict switch
        {
            VerificationVerdict.NoObjects =>
                ("NO DDL DETECTED", $"{VersionLabel(report.Version)} has nothing to verify.", Theme.Warning),
            VerificationVerdict.FullyApplied =>
                ("FULLY APPLIED", $"{VersionLabel(report.Version)} matches source. {report.OkCount}/{report.OkCount} objects.", Theme.Success),
            VerificationVerdict.NotApplied =>
                ("NOT APPLIED", $"{VersionLabel(report.Version)} is missing entirely. 0/{report.Results.Count} objects.", Theme.Danger),
            VerificationVerdict.Partial =>
                ("PARTIALLY APPLIED", $"{VersionLabel(report.Version)}: {report.OkCount} ok · {report.DiffersCount} differs · {report.MissingCount} missing · {report.Results.Count} total.", Theme.Warning),
            _ => ("UNKNOWN", string.Empty, Theme.Warning)
        };

        var body = new Markup(
            $"[bold {colour}]{Theme.Escape(headline)}[/]\n[{Theme.Muted}]{Theme.Escape(subline)}[/]");

        var panel = new Panel(body)
        {
            Border = BoxBorder.Rounded,
            BorderStyle = Theme.BorderStyle,
            Padding = new Padding(2, 0, 2, 0),
            Header = new PanelHeader($"[{colour}] verdict [/]", Justify.Left),
            Expand = false
        };

        AnsiConsole.Write(panel);
    }

    private string BuildVerdictLogLine(VerificationReport report) => report.Verdict switch
    {
        VerificationVerdict.NoObjects    => $"Verdict: {VersionLabel(report.Version)} has no detectable DDL.",
        VerificationVerdict.FullyApplied => $"Verdict: {VersionLabel(report.Version)} fully APPLIED ({report.OkCount}/{report.OkCount}).",
        VerificationVerdict.NotApplied   => $"Verdict: {VersionLabel(report.Version)} NOT APPLIED (0/{report.Results.Count}).",
        VerificationVerdict.Partial      => $"Verdict: {VersionLabel(report.Version)} PARTIALLY APPLIED. OK: {report.OkCount}, Differs: {report.DiffersCount}, Missing: {report.MissingCount}, Total: {report.Results.Count}.",
        _                                => "Verdict: unknown."
    };

    public bool DatabaseExists(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        try
        {
            using var session = SqlSession.Open(_factory, "master");
            using var cmd = session.CreateCommand("SELECT DB_ID(@name)");
            cmd.Parameters.AddWithValue("@name", name);
            var result = cmd.ExecuteScalar();
            return result is not null && result != DBNull.Value;
        }
        catch
        {
            return false;
        }
    }

    public void RunTestDbScripts(IReadOnlyList<TestDatabase> dbs)
    {
        if (dbs.Count == 0)
        {
            AnsiConsole.MarkupLine($"[{Theme.Warning}]No test databases selected.[/]");
            return;
        }

        var locator = new TestDbScriptLocator(AppPaths.TestDbScriptsDir);
        if (!locator.FolderExists)
        {
            AnsiConsole.MarkupLine($"[{Theme.Danger}]Scripts folder not found: {Theme.Escape(AppPaths.TestDbScriptsDir)}[/]");
            AnsiConsole.MarkupLine($"[{Theme.Muted}]Drop one or more .sql files into that folder and try again.[/]");
            return;
        }

        var scripts = locator.List();
        if (scripts.Count == 0)
        {
            AnsiConsole.MarkupLine($"[{Theme.Warning}]No .sql files found in {Theme.Escape(AppPaths.TestDbScriptsDir)}.[/]");
            return;
        }

        AnsiConsole.MarkupLine($"[{Theme.Info}]Running {scripts.Count} script{(scripts.Count == 1 ? "" : "s")} on {dbs.Count} database{(dbs.Count == 1 ? "" : "s")}.[/]");
        AnsiConsole.MarkupLine($"[{Theme.Muted}]Scripts folder: {Theme.Escape(AppPaths.TestDbScriptsDir)}[/]");

        var results = new List<TestDbRunResult>(dbs.Count);

        for (int i = 0; i < dbs.Count; i++)
        {
            var db = dbs[i];
            Banner.Section($"[{i + 1}/{dbs.Count}] {db.Name} ({db.Owner})");

            var perDbLogger = RunLogger.Create(db.Name);
            try
            {
                perDbLogger.Info($"Test DB run started: {db.Name} (owner: {db.Owner})");

                SqlSession? perDbSession = null;
                try
                {
                    perDbSession = SqlSession.Open(_factory, db.Name);
                }
                catch (Exception ex)
                {
                    perDbLogger.Error($"Could not connect: {ex.Message}");
                    AnsiConsole.MarkupLine($"[{Theme.Danger}]FAIL connect to {Theme.Escape(db.Name)}: {Theme.Escape(ex.Message)}[/]");
                    results.Add(new TestDbRunResult(db, Success: false, Passed: 0, Total: scripts.Count, LogFile: perDbLogger.LogFilePath, FirstError: ex.Message));
                    continue;
                }

                using (perDbSession)
                {
                    var perDbRunner = new SqlScriptRunner(perDbSession, perDbLogger, db.Name);
                    int passed = 0;
                    string? firstError = null;

                    foreach (var script in scripts)
                    {
                        var label = script.Name;
                        var ok = perDbRunner.Execute(label, script.FullName);
                        if (ok)
                        {
                            passed++;
                        }
                        else if (firstError is null)
                        {
                            firstError = $"FAIL {label}";
                        }
                    }

                    var success = passed == scripts.Count;
                    results.Add(new TestDbRunResult(db, success, passed, scripts.Count, perDbLogger.LogFilePath, firstError));
                }
            }
            finally
            {
                perDbLogger.Dispose();
            }
        }

        RenderTestDbSummary(results);
    }

    private static void RenderTestDbSummary(IReadOnlyList<TestDbRunResult> results)
    {
        AnsiConsole.WriteLine();
        Banner.Section("Test DB run summary");

        var passed = results.Count(r => r.Success);
        var failed = results.Count - passed;

        var strip = new Grid()
            .AddColumn(new GridColumn().PadRight(4))
            .AddColumn(new GridColumn().PadRight(4))
            .AddColumn(new GridColumn());
        strip.AddRow(
            $"[{Theme.Subtitle}]TOTAL[/]  [bold {Theme.Muted}]{results.Count}[/]",
            $"[{Theme.Success}]PASS[/]   [bold {Theme.Success}]{passed}[/]",
            $"[{Theme.Danger}]FAIL[/]   [bold {Theme.Danger}]{failed}[/]");
        AnsiConsole.Write(strip);
        AnsiConsole.WriteLine();

        var table = new Table
        {
            Border = TableBorder.Horizontal,
            BorderStyle = Theme.BorderStyle,
            ShowRowSeparators = false,
            Expand = false
        };
        table.AddColumn(new TableColumn(string.Empty).NoWrap().Width(11).Padding(0, 1));
        table.AddColumn(new TableColumn($"[{Theme.Subtitle}]Database[/]").NoWrap());
        table.AddColumn(new TableColumn($"[{Theme.Subtitle}]Owner[/]").NoWrap());
        table.AddColumn(new TableColumn($"[{Theme.Subtitle}]Scripts[/]").NoWrap());
        table.AddColumn(new TableColumn($"[{Theme.Subtitle}]Detail[/]"));

        foreach (var r in results)
        {
            var pill = r.Success
                ? $"[{Theme.PillOk}] PASS [/]"
                : $"[{Theme.PillMissing}] FAIL [/]";
            var detail = r.Success
                ? string.Empty
                : $"[{Theme.Muted}]{Theme.Escape(r.FirstError ?? string.Empty)}[/]";
            table.AddRow(
                pill,
                $"[white]{Theme.Escape(r.Database.Name)}[/]",
                $"[{Theme.Muted}]{Theme.Escape(r.Database.Owner)}[/]",
                $"{r.Passed}/{r.Total}",
                detail);
        }

        AnsiConsole.Write(table);

        var failedResults = results.Where(r => !r.Success).ToList();
        if (failedResults.Count == 0)
        {
            return;
        }

        AnsiConsole.WriteLine();
        OfferOpenFailedLog(failedResults);
    }

    private const string CloseSummarySentinel = "__close__";

    private static void OfferOpenFailedLog(IReadOnlyList<TestDbRunResult> failed)
    {
        while (true)
        {
            var choices = new List<string>(failed.Select(r => r.Database.Name)) { CloseSummarySentinel };

            var prompt = new SelectionPrompt<string>
            {
                HighlightStyle = Theme.HighlightStyle,
                PageSize = 15
            }
            .Title($"[bold {Theme.Title}]Pick a failed DB to open its log[/]")
            .UseConverter(choice =>
            {
                if (choice == CloseSummarySentinel)
                {
                    return Theme.MenuRow("←", "Close", "Return to the menu");
                }
                var entry = failed.First(r => r.Database.Name == choice);
                return Theme.MenuRow("✕", entry.Database.Name, entry.Database.Owner);
            })
            .AddChoices(choices);

            var selected = AnsiConsole.Prompt(prompt);
            if (selected == CloseSummarySentinel)
            {
                return;
            }

            var match = failed.First(r => r.Database.Name == selected);
            if (string.IsNullOrWhiteSpace(match.LogFile) || !File.Exists(match.LogFile))
            {
                AnsiConsole.MarkupLine($"[{Theme.Warning}]Log file not found: {Theme.Escape(match.LogFile ?? "(none)")}[/]");
                continue;
            }

            try
            {
                Process.Start(new ProcessStartInfo(match.LogFile) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[{Theme.Danger}]Could not open log: {Theme.Escape(ex.Message)}[/]");
            }
        }
    }

    private sealed record TestDbRunResult(
        TestDatabase Database,
        bool Success,
        int Passed,
        int Total,
        string LogFile,
        string? FirstError);
}
