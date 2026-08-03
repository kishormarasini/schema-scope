using System.Text.Json;
using SchemaScope.Auditing;
using Xunit;

namespace SchemaScope.Tests;

public class SchemaAuditorTests
{
    private static AuditScriptResult Entry(int version, AuditScriptStatus status, int present = 0, int total = 0) =>
        new(version, $"1.0.0.{version}", $"/scripts/1.0.0.{version}.sql", status, present, total, 0,
            Array.Empty<AuditObjectResult>());

    [Fact]
    public void Classify_AllApplied_IsAtHead()
    {
        var entries = new[]
        {
            Entry(1, AuditScriptStatus.Applied, 2, 2),
            Entry(2, AuditScriptStatus.NoDdl),
            Entry(3, AuditScriptStatus.Applied, 1, 1),
        };

        var (verdict, pending, drift, totals) = SchemaAuditor.Classify(entries, highestApplied: 3, startFrom: 0);

        Assert.Equal(AuditVerdict.AtHead, verdict);
        Assert.Empty(pending);
        Assert.Empty(drift);
        Assert.Equal(3, totals.Scripts);
        Assert.Equal(2, totals.Applied);
        Assert.Equal(1, totals.NoDdl);
    }

    [Fact]
    public void Classify_MissingAfterHead_IsBehindHead()
    {
        var entries = new[]
        {
            Entry(1, AuditScriptStatus.Applied, 2, 2),
            Entry(2, AuditScriptStatus.Missing, 0, 3),
        };

        var (verdict, pending, drift, _) = SchemaAuditor.Classify(entries, highestApplied: 1, startFrom: 0);

        Assert.Equal(AuditVerdict.BehindHead, verdict);
        Assert.Single(pending);
        Assert.Equal(2, pending[0].Version);
        Assert.Empty(drift);
    }

    [Fact]
    public void Classify_MissingBeforeHead_IsDriftNotPending()
    {
        var entries = new[]
        {
            Entry(1, AuditScriptStatus.Missing, 0, 2),
            Entry(2, AuditScriptStatus.Applied, 2, 2),
        };

        var (verdict, pending, drift, _) = SchemaAuditor.Classify(entries, highestApplied: 2, startFrom: 0);

        Assert.Equal(AuditVerdict.AtHead, verdict);
        Assert.Empty(pending);
        Assert.Single(drift);
        Assert.Equal(1, drift[0].Version);
    }

    [Fact]
    public void Classify_NothingApplied_FullScan_IsNotInitialised()
    {
        var entries = new[] { Entry(1, AuditScriptStatus.Missing, 0, 1) };

        var (verdict, _, _, _) = SchemaAuditor.Classify(entries, highestApplied: 0, startFrom: 0);

        Assert.Equal(AuditVerdict.NotInitialised, verdict);
    }

    [Fact]
    public void Classify_NothingApplied_PartialScan_IsNoAppliedVersionInRange()
    {
        var entries = new[] { Entry(5, AuditScriptStatus.Missing, 0, 1) };

        var (verdict, _, _, _) = SchemaAuditor.Classify(entries, highestApplied: 0, startFrom: 5);

        Assert.Equal(AuditVerdict.NoAppliedVersionInRange, verdict);
    }

    [Fact]
    public void ExitCode_AtHeadWithDrift_ZeroUnlessStrict()
    {
        var drift = new[] { Entry(1, AuditScriptStatus.Missing, 0, 2) };
        var report = Report(AuditVerdict.AtHead, drift: drift);

        Assert.Equal(0, report.ToExitCode(strict: false));
        Assert.Equal(1, report.ToExitCode(strict: true));
    }

    [Fact]
    public void ExitCode_BehindHead_IsOne()
    {
        var report = Report(AuditVerdict.BehindHead);
        Assert.Equal(1, report.ToExitCode(strict: false));
    }

    [Fact]
    public void ToJson_UsesCamelCaseAndStringEnums()
    {
        var entry = new AuditScriptResult(
            2, "1.0.0.2", "/scripts/1.0.0.2.sql", AuditScriptStatus.Partial, 1, 3, 0,
            new[] { new AuditObjectResult("Table", "dbo", null, "Customers", "Missing", "table not found") });

        var report = Report(AuditVerdict.BehindHead, pending: new[] { entry });
        using var doc = JsonDocument.Parse(report.ToJson());
        var root = doc.RootElement;

        Assert.Equal("behindHead", root.GetProperty("verdict").GetString());
        Assert.Equal("SchemaScope", root.GetProperty("tool").GetString());

        var pending = root.GetProperty("pending")[0];
        Assert.Equal("partial", pending.GetProperty("status").GetString());
        Assert.Equal(3, pending.GetProperty("objectsTotal").GetInt32());

        var mismatch = pending.GetProperty("mismatches")[0];
        Assert.Equal("dbo", mismatch.GetProperty("schema").GetString());
        Assert.Equal("Customers", mismatch.GetProperty("name").GetString());
        Assert.Equal("Missing", mismatch.GetProperty("status").GetString());
    }

    private static AuditReport Report(
        AuditVerdict verdict,
        IReadOnlyList<AuditScriptResult>? pending = null,
        IReadOnlyList<AuditScriptResult>? drift = null)
    {
        pending ??= Array.Empty<AuditScriptResult>();
        drift ??= Array.Empty<AuditScriptResult>();
        var scripts = pending.Concat(drift).OrderBy(e => e.Version).ToList();

        return new AuditReport(
            Tool: "SchemaScope",
            ToolVersion: "0.1.0",
            ScannedAtUtc: new DateTime(2026, 8, 3, 0, 0, 0, DateTimeKind.Utc),
            Server: "localhost",
            Database: "MyDb",
            ScriptsFolder: "/scripts",
            Scheme: "1.0.0.{0}",
            StartFrom: 0,
            Head: scripts.Count == 0 ? 1 : scripts[^1].Version,
            PartialScan: false,
            Verdict: verdict,
            CurrentVersion: 1,
            CurrentVersionLabel: "1.0.0.1",
            Totals: new AuditTotals(scripts.Count, 0, 0, scripts.Count, 0, 0),
            Pending: pending,
            Drift: drift,
            Scripts: scripts);
    }
}
