using Microsoft.Data.SqlClient;
using SchemaScope.Sql;

namespace SchemaScope.Auditing;

public static class JournalReader
{
    public const string DbUpTable        = "dbo.SchemaVersions";
    public const string FlywayTable      = "dbo.flyway_schema_history";
    public const string SchemaScopeTable = HistoryTracker.Table;

    public static string? DetectProvider(SqlSession session)
    {
        if (TableExists(session, SchemaScopeTable))
        {
            return "schemascope";
        }
        if (TableExists(session, DbUpTable))
        {
            return "dbup";
        }
        if (TableExists(session, FlywayTable))
        {
            return "flyway";
        }
        return null;
    }

    public static (string Table, IReadOnlyList<JournalEntry> Entries)? Read(SqlSession session, string provider)
    {
        return provider.ToLowerInvariant() switch
        {
            "dbup"        => ReadDbUp(session),
            "flyway"      => ReadFlyway(session),
            "schemascope" => ReadSchemaScope(session),
            _ => throw new ArgumentException($"Unknown journal provider '{provider}'. Use dbup, flyway, schemascope, or auto.")
        };
    }

    private static (string, IReadOnlyList<JournalEntry>)? ReadSchemaScope(SqlSession session)
    {
        if (!TableExists(session, SchemaScopeTable))
        {
            return null;
        }

        var entries = new List<JournalEntry>();
        using var cmd = session.CreateCommand(
            "SELECT ScriptName, CAST([Version] AS NVARCHAR(20)), AppliedAtUtc, Success FROM dbo.schemascope_history");
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            entries.Add(new JournalEntry(
                ScriptName: reader.GetString(0),
                Version: reader.IsDBNull(1) ? null : reader.GetString(1),
                AppliedAtUtc: reader.IsDBNull(2) ? null : reader.GetDateTime(2),
                Success: !reader.IsDBNull(3) && reader.GetBoolean(3)));
        }
        return (SchemaScopeTable, entries);
    }

    private static (string, IReadOnlyList<JournalEntry>)? ReadDbUp(SqlSession session)
    {
        if (!TableExists(session, DbUpTable))
        {
            return null;
        }

        var entries = new List<JournalEntry>();
        using var cmd = session.CreateCommand("SELECT ScriptName, Applied FROM dbo.SchemaVersions");
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            entries.Add(new JournalEntry(
                ScriptName: reader.GetString(0),
                Version: null,
                AppliedAtUtc: reader.IsDBNull(1) ? null : reader.GetDateTime(1),
                Success: true));
        }
        return (DbUpTable, entries);
    }

    private static (string, IReadOnlyList<JournalEntry>)? ReadFlyway(SqlSession session)
    {
        if (!TableExists(session, FlywayTable))
        {
            return null;
        }

        var entries = new List<JournalEntry>();
        using var cmd = session.CreateCommand(
            "SELECT script, [version], installed_on, success FROM dbo.flyway_schema_history WHERE [type] <> 'SCHEMA'");
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            entries.Add(new JournalEntry(
                ScriptName: reader.GetString(0),
                Version: reader.IsDBNull(1) ? null : reader.GetString(1),
                AppliedAtUtc: reader.IsDBNull(2) ? null : reader.GetDateTime(2),
                Success: !reader.IsDBNull(3) && reader.GetBoolean(3)));
        }
        return (FlywayTable, entries);
    }

    private static bool TableExists(SqlSession session, string table)
    {
        using var cmd = session.CreateCommand("SELECT OBJECT_ID(@table, 'U')");
        cmd.Parameters.Add(new SqlParameter("@table", table));
        return cmd.ExecuteScalar() is not DBNull and not null;
    }

    public static JournalAudit CrossCheck(
        string provider,
        string table,
        IReadOnlyList<JournalEntry> entries,
        AuditReport audit)
    {
        var matchedVersions = new HashSet<int>();

        var journaledButNotApplied = new List<JournalFinding>();
        var unmatched = new List<JournalFinding>();
        var failed = new List<JournalFinding>();

        foreach (var entry in entries)
        {
            if (!entry.Success)
            {
                failed.Add(new JournalFinding(entry.ScriptName, null,
                    $"{provider} recorded this migration as FAILED{At(entry)}."));
            }

            var script = Match(entry, audit.Scripts);
            if (script is null)
            {
                unmatched.Add(new JournalFinding(entry.ScriptName, null,
                    "No local script file matches this journal entry (renamed, deleted, or outside the scanned range)."));
                continue;
            }

            matchedVersions.Add(script.Version);

            var pendingButJournaled = entry.Success
                && script.Version > audit.CurrentVersion
                && script.Status is AuditScriptStatus.Missing or AuditScriptStatus.Partial;
            if (pendingButJournaled)
            {
                journaledButNotApplied.Add(new JournalFinding(entry.ScriptName, script.Label,
                    $"Journal says applied{At(entry)}, but {script.ObjectsTotal - script.ObjectsPresent} of {script.ObjectsTotal} objects are absent from the schema."));
            }
        }

        var appliedButNotJournaled = audit.Scripts
            .Where(s => !matchedVersions.Contains(s.Version)
                && s.Status is AuditScriptStatus.Applied or AuditScriptStatus.NoDdl)
            .Select(s => new JournalFinding(System.IO.Path.GetFileName(s.File), s.Label,
                s.Status == AuditScriptStatus.Applied
                    ? "All objects exist in the schema, but the journal has no record of this script running."
                    : "Script has no DDL to verify and no journal record — it may never have run."))
            .ToList();

        return new JournalAudit(
            Provider: provider,
            Table: table,
            Entries: entries.Count,
            JournaledButNotApplied: journaledButNotApplied,
            AppliedButNotJournaled: appliedButNotJournaled,
            UnmatchedJournalEntries: unmatched,
            FailedJournalEntries: failed);

        static string At(JournalEntry e) =>
            e.AppliedAtUtc is { } at ? $" on {at:yyyy-MM-dd}" : string.Empty;
    }

    internal static AuditScriptResult? Match(JournalEntry entry, IReadOnlyList<AuditScriptResult> scripts)
    {
        foreach (var script in scripts)
        {
            var fileName = System.IO.Path.GetFileName(script.File);
            if (entry.ScriptName.EndsWith(fileName, StringComparison.OrdinalIgnoreCase))
            {
                return script;
            }
        }

        if (entry.Version is not null
            && int.TryParse(entry.Version.Split('.')[^1], out var versionNumber))
        {
            return scripts.FirstOrDefault(s => s.Version == versionNumber);
        }

        return null;
    }
}
