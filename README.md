# SchemaScope

[![CI](https://github.com/kishormarasini/schema-scope/actions/workflows/ci.yml/badge.svg)](https://github.com/kishormarasini/schema-scope/actions/workflows/ci.yml)

A modern CLI for running, verifying, and cloning versioned SQL Server databases.

SchemaScope runs a folder of numbered `.sql` migration scripts in order and, uniquely, **tells you whether each script is actually applied** by parsing it with Microsoft's T-SQL parser (`ScriptDom`) and comparing object-by-object against the live database. It also handles backup, restore, clone, and detecting the version a database is currently at.

Built on **.NET 10**, styled with [Spectre.Console](https://spectreconsole.net/) using the [Catppuccin Mocha](https://github.com/catppuccin/catppuccin) palette, and powered by [Microsoft.SqlServer.TransactSql.ScriptDom](https://learn.microsoft.com/en-us/dotnet/api/microsoft.sqlserver.transactsql.scriptdom) for parsing and canonical regeneration.

**[See SchemaScope in action](https://project.kishormarasini.com.au/schemascope)**: an annotated walkthrough of detect, heal, verify, backup, and clone.

---

## Features

| | |
|---|---|
| **Detect version** | Probes the DB to find its current applied version. Walks every script in the chosen range (start-from is configurable), classifies each as Applied / Partial / Missing / NoDdl, and reports the highest fully-applied version. Distinguishes genuine pending work from historical drift caused by later drops/renames. |
| **Verify** | Deep-compares a specific version's objects to the DB: tables, columns (type + nullability), indexes (keys + INCLUDE), constraints, and procedure / view / trigger / function bodies. Body comparison parses both sides through ScriptDom and regenerates canonical text so whitespace, bracket style, keyword case, and `CREATE OR ALTER` never cause false diffs. |
| **Full heal** | Runs a prepatch file (if any) then every version script in a range. |
| **Versions** | Runs a range of scripts without the prepatch. |
| **Specific version** | Runs one version by number. |
| **Prepatch** | Runs a one-off idempotent pre-step SQL file. |
| **Backup** | `BACKUP DATABASE` to a `.bak` inside the app folder with live progress. |
| **Restore** | Restores a `.bak` into a target DB. Auto-discovers logical file names (`RESTORE FILELISTONLY`), auto-uses server default data/log paths, forces `SINGLE_USER WITH ROLLBACK IMMEDIATE` if the target already exists, then `MULTI_USER` after. |
| **Clone** | Backup source → Restore as target, in one step. |
| **Setup wizard** | First run opens a guided setup: server, authentication (with a live connection test), database picker straight from `sys.databases`, script-folder validation with a live count of matching files, and naming-scheme presets. Rerun it any time from the **Settings** menu or with `--setup`. |
| **Audit** | One command, the whole truth: `--audit` probes every script in range and emits a full drift report — applied / partial / missing per script, object-level mismatch detail, drift vs pending split, and a machine-readable JSON payload (`--output json`, `--report file.json`) for pipelines and dashboards. |
| **Journal cross-check** | Already on DbUp or Flyway? `--journal auto` reads their history table (`SchemaVersions` / `flyway_schema_history`) and checks every claim against the live schema. Catches **journal lies** (recorded as applied, objects absent), **untracked scripts** (applied by hand, never journaled), **orphaned entries**, and failed Flyway migrations. Zero risk, read-only — drop it into CI next to the tools you already run. |

**Small quality-of-life touches**: a grouped, searchable menu (arrow keys or type to filter); typing `back`, `menu`, or `cancel` at any prompt returns to the main menu; pre-flight input validation; retry-with-edits loops on operation failures; completion panels with `OSC 52` clipboard auto-copy and `OSC 8` folder hyperlinks; per-user Debug config for F5-without-typing.

**Security**: SQL-auth passwords never have to touch disk. The wizard offers three options — the `SCHEMASCOPE_PASSWORD` environment variable (recommended), a masked once-per-session prompt, or (opt-in, with a warning) storing it in the per-user config file. Session-entered passwords are guaranteed never to be written back on save.

---

## Requirements

- **.NET 10 SDK** (for building or running; pinned via [`global.json`](global.json))
- SQL Server reachable via Windows Authentication (on Windows) or SQL authentication (any OS)
- Runs on Windows, Linux, or macOS. Visual Studio 2026 (18.0+), Rider, or the `dotnet` CLI

Built with `<Nullable>enable</Nullable>` and `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`; the compiler enforces null annotations across every public and internal API. All DB reads, path helpers, file I/O, JSON deserialisation, and reflection-based assembly metadata are null-guarded.

---

## Getting started

### Run from Visual Studio
1. Open `SchemaScope.sln`.
2. F5. Interactive menu comes up.

### Run from the command line
```bash
# Interactive. First run opens a guided setup wizard (server, auth, database,
# scripts folder) with a live connection test; everything is saved for next time.
dotnet run --project src/SchemaScope

# Rerun the wizard any time (also available as "Settings" in the menu).
dotnet run --project src/SchemaScope -- --setup

# Release publish: single-file, self-contained, no .NET install needed on the target.
# Choose the runtime for the target OS: win-x64, linux-x64, osx-x64, or osx-arm64.
dotnet publish src/SchemaScope -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
# Output: src/SchemaScope/bin/Release/net10.0/win-x64/publish/schemascope(.exe)
```

### Non-interactive
Pass `--database` to enter scripted mode.

```powershell
schemascope --database MyDb --server MACHINE\INSTANCE --version-folder D:\migrations --start-from 1 --end-at 9999
```

| Flag | Description |
|---|---|
| `-d`, `--database` | Target database. Triggers non-interactive mode. |
| `-s`, `--server` | SQL instance (e.g. `MACHINE\INSTANCE` or `.`). |
| `-u`, `--user-id` | SQL login user; implies SQL authentication. Password comes from `SCHEMASCOPE_PASSWORD`. No config file needed. |
| `-f`, `--version-folder` | Folder containing the versioned `.sql` scripts. |
| `-c`, `--config` | Path to a config file. Defaults to the per-user config (see below). |
| `--prepatch-file` | Optional idempotent pre-step SQL. |
| `--start-from` | Lowest version to run. `0` = no bound. |
| `--end-at` | Highest version to run. `0` = no bound. |
| `--skip-prepatch` | Skip prepatch; run versions only. |
| `--prepatch-only` | Run prepatch only. |
| `--setup` | Run the interactive setup wizard. |
| `-h`, `--help` | Show help. |
| `-v`, `--version` | Print app name, version, author, and license, then exit. |

Every flag is optional when the value is present in the config file; CLI flags override config values for that run.

### Operations (verify, detect, backup, restore, clone)

The same operations available in the interactive menu can be run directly from the command line, which makes them scriptable in CI/CD.

```bash
# Verify a version against the DB. Read-only. Exit code 0 = fully applied, 1 = drift (CI-friendly).
schemascope --verify 42 --database MyDb --server MACHINE\INSTANCE --version-folder ./migrations

# Detect the current applied version.
schemascope --detect --database MyDb --start-from 1

# Full drift audit: every script in range, object-level detail, JSON for pipelines.
# Exit 0 = at head, 1 = behind (add --strict to also fail on drift).
schemascope --audit --database MyDb --output json --report drift-report.json

# Audit a database managed by DbUp or Flyway: cross-check their journal
# against the actual schema. Exit 1 if the journal claims anything that isn't true.
schemascope --audit --database MyDb --journal auto

# Backup / Restore / Clone (no scripts folder needed).
schemascope --backup  --source MyDb --backup-path ./backups/MyDb.bak
schemascope --restore --target MyDb_Restored --backup-path ./backups/MyDb.bak
schemascope --clone   --source MyDb --target MyDb_Copy
```

| Flag | Description |
|---|---|
| `--verify <n>` | Verify version `<n>` against the DB. Read-only. Exits `0` if fully applied, `1` on drift/missing, `2` on bad input. |
| `--detect` | Detect the highest fully-applied version. Honours `--start-from`. |
| `--audit` | Probe **every** script in range and report drift + pending with object-level detail. Exits `0` at head, `1` behind. |
| `-o`, `--output <fmt>` | Output for `--verify` / `--detect` / `--audit`: `text` (default) or `json`. JSON goes to stdout, diagnostics to stderr — pipe-safe. |
| `--report <path>` | Also write the JSON report to a file (works in text mode too). |
| `--strict` | With `--audit`: exit `1` when drift exists, even at head. Also fails on untracked/orphaned journal findings. |
| `--journal <p>` | With `--audit`: cross-check a migration journal — `dbup`, `flyway`, or `auto` (detects whichever table exists). Journal lies always exit `1`. |
| `--backup` | Back up a database. Requires `--source` and `--backup-path`. |
| `--restore` | Restore a backup. Requires `--target` and `--backup-path`; optional `--data-dir` / `--log-dir`. |
| `--clone` | Clone a database. Requires `--source` and `--target`; optional `--backup-path`. |
| `--source <db>` | Source database (backup / clone). |
| `--target <db>` | Target database (restore / clone). |
| `--backup-path <p>` | Path to the `.bak` file. |
| `--data-dir <p>` / `--log-dir <p>` | Override restore data/log file directories. |

Only one operation can run per invocation. The `--verify` and `--audit` exit codes make them drop-in **read-only gates for CI pipelines** (e.g. fail a deploy when the target DB has drifted from the expected version).

#### JSON report shape

`--output json` prints a single JSON document to stdout (diagnostics go to stderr). The audit payload includes the verdict, current/head versions, per-status totals, and the full per-script breakdown with object-level mismatches:

```json
{
  "tool": "SchemaScope",
  "verdict": "behindHead",
  "currentVersion": 2,
  "currentVersionLabel": "1.0.0.2",
  "head": 3,
  "totals": { "scripts": 3, "applied": 1, "partial": 1, "missing": 1, "noDdl": 0, "withParseErrors": 0 },
  "pending": [
    {
      "version": 3, "label": "1.0.0.3", "status": "missing",
      "objectsPresent": 0, "objectsTotal": 3,
      "mismatches": [
        { "kind": "Table", "schema": "dbo", "name": "Invoices", "status": "Missing", "detail": "table not present" }
      ]
    }
  ],
  "drift": [ { "version": 1, "label": "1.0.0.1", "status": "partial" } ]
}
```

Unlike journal-based tools (DbUp, Flyway), the audit never trusts a history table — every claim is verified against the live schema, so a hand-edited database or half-failed script can't hide.

### GitHub Action

The repo doubles as a composite action. It builds the CLI, runs the audit, writes a rich job summary (verdict, totals, pending scripts, journal findings), and exposes outputs:

```yaml
- uses: kishormarasini/schema-scope@main
  env:
    SCHEMASCOPE_PASSWORD: ${{ secrets.DB_PASSWORD }}
  with:
    server: sql.internal,1433
    database: MyDb
    version-folder: ./migrations
    user-id: ci_readonly
    journal: auto          # optional: cross-check DbUp/Flyway
    strict: "false"
```

Outputs: `verdict`, `current-version`, `pending-count`, `report-path`. The job fails when the database is behind head or the journal lies — a read-only drift gate with zero deploy risk.

### Docker

```bash
docker run --rm -e SCHEMASCOPE_PASSWORD -v ./migrations:/migrations \
  ghcr.io/kishormarasini/schemascope:latest \
  --audit --server host.docker.internal --database MyDb \
  --version-folder /migrations --user-id sa --output json
```

Images are published to GHCR on every release tag.

### Script convention
By default, files are named `1.0.0.N.sql` where `N` is a positive integer (`1.0.0.1.sql`, `1.0.0.2.sql`, ...). The naming scheme is **not hard-coded**: it's defined by `VersionScheme` in the config file, so a `V12__add_users.sql`-style convention works just as well (see [Configuration](#configuration)). Scripts may reference the target database via the `[DatabaseName]` placeholder, which SchemaScope substitutes textually before execution.

---

## Configuration

All settings live in a single JSON config file: server, database, script folder, prepatch path, the version-file naming scheme, the default schema, and connection/auth options. Nothing is hard-coded. CLI flags override individual values at runtime.

You rarely need to edit the file by hand: the first interactive run opens a guided setup wizard, and the **Settings** menu (or `--setup`) reruns it with your current values pre-filled — including a live connection test and a database picker.

The file is resolved in this order:
1. `--config <path>` if supplied.
2. `%APPDATA%\SchemaScope\config.json` (the per-user file; written back after each successful run so the next run pre-fills).
3. `config.json` next to the executable, used as a read-only template to seed the per-user file on first run.

A documented template ships as [`config.sample.json`](config.sample.json):

```json
{
  "Server": "MACHINE\\INSTANCE",
  "Database": "MyDb",
  "VersionFolder": "D:\\path\\to\\versioned\\scripts",
  "PrepatchFile": "",
  "DefaultSchema": "dbo",
  "VersionScheme": {
    "FilePattern": "^1\\.0\\.0\\.(\\d+)\\.sql$",
    "FileNameFormat": "1.0.0.{0}.sql",
    "LabelFormat": "1.0.0.{0}"
  },
  "Connection": {
    "Authentication": "Windows",
    "UserId": "",
    "Password": "",
    "Encrypt": false,
    "TrustServerCertificate": true,
    "ConnectTimeoutSeconds": 15,
    "CommandTimeoutSeconds": 600
  }
}
```

| Key | Purpose |
|---|---|
| `Server` / `Database` / `VersionFolder` / `PrepatchFile` | Connection target and script locations. |
| `DefaultSchema` | Schema assumed for objects that don't qualify their names (default `dbo`). |
| `VersionScheme.FilePattern` | Regex with **one capturing group** that yields the integer version number. |
| `VersionScheme.FileNameFormat` / `LabelFormat` | `String.Format` templates (`{0}` = version number) for building a file name and a display label. |
| `Connection.Authentication` | `Windows` (integrated) or `SqlPassword` (uses `UserId` / `Password`). |
| `Connection.Password` | Optional. Prefer the `SCHEMASCOPE_PASSWORD` environment variable, which takes precedence and keeps the password out of the config file. |
| `Connection.Encrypt` / `TrustServerCertificate` / `ConnectTimeoutSeconds` / `CommandTimeoutSeconds` | Connection-string knobs applied to every connection. |

---

## How version detection works

SchemaScope walks **every** script in the configured range, classifies each one, and derives the DB's current version from the highest fully-applied script (not from the first gap). This tolerates the normal schema-drift that happens in any long-lived migration set, where later versions drop or rename objects declared in earlier versions.

```
for v in startFrom..head:
    objects = DdlExtractor.Extract(script_v)     # ScriptDom AST walk
    results = VersionVerifier.Verify(objects)    # object-by-object DB check
    classify → { Applied | Partial | Missing | NoDdl }
    if Applied or NoDdl: highestApplied = v

current  = highestApplied
pending  = { v > highestApplied : status != Applied }    # real work to do
drift    = { v < highestApplied : status != Applied }    # cosmetic / historical
```

### Verdict

| Verdict | Condition |
|---|---|
| **AT HEAD** | `highestApplied == head` and no pending |
| **BEHIND HEAD** | `highestApplied < head`, pending > 0 |
| **DATABASE NOT INITIALISED** | `highestApplied == 0` (full scan from 1) |
| **NO APPLIED VERSION IN RANGE** | `highestApplied == 0` (partial scan starting above 1) |

Drift is reported as an informational subline, never as a blocker.

### Start-from

The menu prompts for a starting version number (default `1`). Entering a higher number skips the earliest scripts, useful for known mid-range DBs where a full scan would be slow. When the scan is partial, the verdict labels it "Partial scan: started at 1.0.0.N. Versions below that were not checked."

### Details

- **Data-only scripts** (no DDL) inherit the previous verdict; they always match.
- **Module bodies** are compared via ScriptDom parse + canonical regeneration; `CREATE` vs `CREATE OR ALTER` vs `ALTER` are treated as equivalent because SQL Server stores all three the same way.
- Per-script status goes to the file log (`%APPDATA%\SchemaScope\logs\*.log`); the UI shows only counts and non-Applied items so large probes stay readable.

See [`Parsing/DdlExtractor.cs`](src/SchemaScope/Parsing/DdlExtractor.cs), [`Verification/VersionVerifier.cs`](src/SchemaScope/Verification/VersionVerifier.cs), and [`ToolkitActions.DetectCurrentVersion`](src/SchemaScope/ToolkitActions.cs).

---

## Paths

Everything SchemaScope writes lives under `%APPDATA%\SchemaScope\` plus one folder next to the exe:

| Path | Purpose |
|---|---|
| `%APPDATA%\SchemaScope\config.json` | The full config file (see [Configuration](#configuration)); updated after each successful run. |
| `%APPDATA%\SchemaScope\logs\schemascope_<DB>_<timestamp>.log` | Per-run plain-text log. |
| `%APPDATA%\SchemaScope\logs\error.log` | Rolling error log: unhandled exceptions and config / registry read-write failures, with timestamp and stack trace. |
| `%APPDATA%\SchemaScope\logs\diffs\*.sql` | Raw + ScriptDom-canonicalised dumps of differing module bodies, for manual diffing. |
| `<app folder>\Backups\<DB>_<timestamp>.bak` | Output of every Backup and the intermediate `.bak` for Clone. |

---

## Logging & error handling

- **Per-run log.** Every operation opens a `RunLogger` that writes to the console and a timestamped file under `logs\`. Failures inside an operation are caught with specific exception types (`SqlException`, `IOException`, ...), surfaced in the UI, and recorded in that run's log.
- **Error log.** `logs\error.log` is a rolling sink for anything that happens outside a run: unhandled exceptions (caught by a top-level guard in `Program`), and config / test-DB-registry read or write failures. Each entry has a timestamp, the exception type and message, and a stack trace.
- **Best-effort writes never abort the run.** Config and registry persistence, clipboard copy, and log writes are best-effort: a failure is logged to `error.log` (where it matters) and execution continues.
- **Exit codes.** `0` success · `1` unhandled error (see `error.log`) · `2` bad arguments / missing required input · `3` cannot connect.

---

## Project layout

```
SchemaScope/
  SchemaScope.sln
  README.md
  config.sample.json               documented config template
  .gitignore
  src/SchemaScope/
    SchemaScope.csproj
    Program.cs                       entry, CLI parsing, config load, wiring
    AppInfo.cs                       product name, version, author, license
    LICENSE                          (repo root) MIT license text
    AppPaths.cs                      %APPDATA% paths
    ToolkitActions.cs                orchestrates every menu action + progress rendering
    Cli/CliOptions.cs                hand-rolled arg parser
    Configuration/
      SchemaScopeConfig.cs           the single backend config file (load / save / discovery)
      ConnectionSettings.cs          auth mode, encryption, timeouts -> connection string
      VersionScheme.cs               configurable version-file pattern / label / file-name
    Parsing/
      DdlExtractor.cs                ScriptDom visitor: extracts tables, columns (with types),
                                     indexes, constraints, procs, views, triggers, fns; schema-aware
      DdlObject.cs / DdlObjectKind.cs
      ModuleBodyComparer.cs          parse-and-regenerate body equality via Sql160ScriptGenerator
      SqlNormalizer.cs               tiny helper for column / index spec strings
    Sql/
      SqlConnectionFactory.cs        single place that builds connections from ConnectionSettings
      SqlSession.cs                  managed SqlConnection + helpers
      SqlScriptRunner.cs             loads a .sql, substitutes [DatabaseName], runs each batch
      SqlBatchSplitter.cs            GO separator
      VersionScriptLocator.cs        enumerates version scripts per the configured scheme
      VersionScript.cs
      DatabaseOperations.cs          BACKUP / RESTORE / CLONE with STATS progress
    Ui/
      Banner.cs                      title, section rules, connection info grid
      Prompts.cs                     text prompts, validators, menu selection
      RunLogger.cs                   console + file logger
      Theme.cs                       Catppuccin Mocha palette
      MenuAction.cs
      InteractiveShell.cs            menu loop, action flows
      ReturnToMenuException.cs       thrown by any prompt on "back" / "menu" / "cancel"
    Verification/
      VersionVerifier.cs             per-object DB checks (schema-qualified, parameterised)
      VerificationResult.cs
  tests/SchemaScope.Tests/           xUnit tests for the parser, comparer, locator, scheme
```

---

## Roadmap

- [x] Run versioned scripts (full heal, range, specific)
- [x] Verify a version against the DB
- [x] Detect current version
- [x] Backup / Restore / Clone
- [x] Configurable version-file scheme (no hard-coded `1.0.0.N`)
- [x] Schema-aware verification (objects outside `dbo`)
- [x] SQL authentication (`Windows` or `SqlPassword` via the config file)
- [x] Unit tests for the parsing / comparison / locator layers
- [x] CLI flags for `--backup` / `--restore` / `--clone`
- [x] Read-only verify mode for CI pipelines
- [x] Cross-platform (Linux / macOS): drop Windows-only assumptions
- [x] CI: build + test on Linux, Windows, and macOS via GitHub Actions
- [x] `SCHEMASCOPE_PASSWORD` environment variable (keeps SQL auth passwords out of the config file)
- [x] First-run setup wizard with live connection test and database picker
- [x] In-app Settings (rerun the wizard, reconnect without restarting)
- [x] Session-only masked password entry (never written to disk)
- [x] `--audit`: full-range drift report with object-level detail
- [x] `--output json` / `--report` machine-readable output for verify, detect, and audit
- [x] Journal cross-check: audit databases managed by DbUp / Flyway (`--journal auto`)
- [x] GitHub Action (composite, with job summary + outputs) and GHCR container image
- [ ] Opt-in history table (tracks data-only scripts; verification stays evidence-based)

---

## Conventions

When contributing, follow these repo rules:

- **Comments are minimal.** Only keep a comment when it documents non-obvious external behavior whose loss would risk a regression (for example, why module bodies fold `CREATE OR ALTER` to `CREATE`, or the `(max)` / `max_length = -1` column-spec rule). Do not add comments that merely restate what the code already says; put that rationale in the commit message instead.
- **Nullable + warnings-as-errors.** The project builds with `<Nullable>enable</Nullable>` and `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`; keep it warning-clean.
- **Nothing hard-coded that belongs in config.** Server, database, script folder, version-file scheme, default schema, and connection/auth settings all live in the config file.

---

## License

MIT. See [LICENSE](LICENSE).

---

## Author

**Kishor Marasini** ([@kishormarasini](https://github.com/kishormarasini))

Issues and PRs welcome.
