using SchemaScope.Auditing;
using Xunit;

namespace SchemaScope.Tests;

public class JournalCrossCheckTests
{
    private static AuditScriptResult Script(int version, AuditScriptStatus status, int present = 0, int total = 0) =>
        new(version, $"1.0.0.{version}", $"/scripts/1.0.0.{version}.sql", status, present, total, 0,
            Array.Empty<AuditObjectResult>());

    private static JournalEntry Journaled(string scriptName, bool success = true, string? version = null) =>
        new(scriptName, version, new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc), success);

    private static AuditReport Audit(int currentVersion, params AuditScriptResult[] scripts) =>
        new("SchemaScope", "0.1.0", DateTime.UtcNow, "srv", "db", "/scripts", "1.0.0.{0}",
            0, scripts[^1].Version, false,
            currentVersion == scripts[^1].Version ? AuditVerdict.AtHead : AuditVerdict.BehindHead,
            currentVersion, $"1.0.0.{currentVersion}",
            new AuditTotals(scripts.Length, 0, 0, 0, 0, 0),
            scripts.Where(s => s.Version > currentVersion && s.Status != AuditScriptStatus.Applied).ToList(),
            scripts.Where(s => s.Version < currentVersion && s.Status != AuditScriptStatus.Applied).ToList(),
            scripts);

    [Fact]
    public void JournaledPendingScript_IsReportedAsLie()
    {
        var scripts = new[]
        {
            Script(1, AuditScriptStatus.Applied, 2, 2),
            Script(2, AuditScriptStatus.Missing, 0, 3),
        };
        var audit = Audit(currentVersion: 1, scripts);
        var entries = new[] { Journaled("Scripts.1.0.0.1.sql"), Journaled("Scripts.1.0.0.2.sql") };

        var result = JournalReader.CrossCheck("dbup", "dbo.SchemaVersions", entries, audit);

        Assert.True(result.HasLies);
        var lie = Assert.Single(result.JournaledButNotApplied);
        Assert.Equal("1.0.0.2", lie.Label);
        Assert.Contains("3 of 3 objects", lie.Detail);
    }

    [Fact]
    public void DriftedScript_JournaledAndDroppedLater_IsNotALie()
    {
        var scripts = new[]
        {
            Script(1, AuditScriptStatus.Partial, 1, 2),
            Script(2, AuditScriptStatus.Applied, 2, 2),
        };
        var audit = Audit(currentVersion: 2, scripts);
        var entries = new[] { Journaled("1.0.0.1.sql"), Journaled("1.0.0.2.sql") };

        var result = JournalReader.CrossCheck("dbup", "dbo.SchemaVersions", entries, audit);

        Assert.False(result.HasLies);
        Assert.Empty(result.JournaledButNotApplied);
    }

    [Fact]
    public void AppliedScriptWithoutJournalRow_IsUntracked()
    {
        var scripts = new[]
        {
            Script(1, AuditScriptStatus.Applied, 2, 2),
            Script(2, AuditScriptStatus.Applied, 2, 2),
        };
        var audit = Audit(currentVersion: 2, scripts);
        var entries = new[] { Journaled("1.0.0.1.sql") };

        var result = JournalReader.CrossCheck("dbup", "dbo.SchemaVersions", entries, audit);

        var untracked = Assert.Single(result.AppliedButNotJournaled);
        Assert.Equal("1.0.0.2", untracked.Label);
    }

    [Fact]
    public void JournalEntryWithNoLocalScript_IsOrphaned()
    {
        var scripts = new[] { Script(1, AuditScriptStatus.Applied, 1, 1) };
        var audit = Audit(currentVersion: 1, scripts);
        var entries = new[] { Journaled("1.0.0.1.sql"), Journaled("9.9.9.99_renamed_away.sql") };

        var result = JournalReader.CrossCheck("dbup", "dbo.SchemaVersions", entries, audit);

        var orphan = Assert.Single(result.UnmatchedJournalEntries);
        Assert.Equal("9.9.9.99_renamed_away.sql", orphan.ScriptName);
    }

    [Fact]
    public void FailedFlywayRow_IsReported()
    {
        var scripts = new[] { Script(1, AuditScriptStatus.Applied, 1, 1) };
        var audit = Audit(currentVersion: 1, scripts);
        var entries = new[] { Journaled("1.0.0.1.sql", success: false) };

        var result = JournalReader.CrossCheck("flyway", "dbo.flyway_schema_history", entries, audit);

        var failure = Assert.Single(result.FailedJournalEntries);
        Assert.Contains("FAILED", failure.Detail);
        Assert.False(result.IsClean);
    }

    [Fact]
    public void Match_EmbeddedResourceName_MatchesBySuffix()
    {
        var scripts = new[] { Script(5, AuditScriptStatus.Applied, 1, 1) };
        var entry = Journaled("MyApp.Migrations.Scripts.1.0.0.5.sql");

        var matched = JournalReader.Match(entry, scripts);

        Assert.NotNull(matched);
        Assert.Equal(5, matched!.Version);
    }

    [Fact]
    public void ExitCode_JournalLies_IsOneEvenAtHead()
    {
        var scripts = new[] { Script(1, AuditScriptStatus.Applied, 1, 1) };
        var audit = Audit(currentVersion: 1, scripts);
        var lie = new JournalFinding("x.sql", "1.0.0.1", "lie");
        var journal = new JournalAudit("dbup", "dbo.SchemaVersions", 1,
            new[] { lie }, Array.Empty<JournalFinding>(), Array.Empty<JournalFinding>(), Array.Empty<JournalFinding>());

        var withJournal = audit with { Journal = journal };

        Assert.Equal(0, audit.ToExitCode(strict: false));
        Assert.Equal(1, withJournal.ToExitCode(strict: false));
    }
}
