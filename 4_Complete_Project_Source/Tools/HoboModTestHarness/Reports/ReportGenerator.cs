using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HoboModTestHarness.Reports;

/// <summary>
/// Complete test results for reporting
/// </summary>
public class TestReport
{
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public string HarnessVersion { get; set; } = "1.0.0";
    public TimeSpan TotalDuration { get; set; }
    public bool OverallSuccess { get; set; }
    
    public BuildReport? Build { get; set; }
    public UnitTestReport? UnitTests { get; set; }
    public SchemaReport? Schemas { get; set; }
    public ModValidationReport? ModValidation { get; set; }
    public InGameReport? InGameTests { get; set; }
    public LogReport? LogVerification { get; set; }
}

public class BuildReport
{
    public bool Success { get; set; }
    public double DurationSeconds { get; set; }
    public string? ErrorMessage { get; set; }
}

public class UnitTestReport
{
    public bool Success { get; set; }
    public int Total { get; set; }
    public int Passed { get; set; }
    public int Failed { get; set; }
    public int Skipped { get; set; }
    public double DurationSeconds { get; set; }
    public List<string> FailedTests { get; set; } = new();
}

public class SchemaReport
{
    public bool Success { get; set; }
    public List<string> GeneratedSchemas { get; set; } = new();
}

public class ModValidationReport
{
    public bool Success { get; set; }
    public int TotalMods { get; set; }
    public int ValidMods { get; set; }
    public int InvalidMods { get; set; }
    public double DurationMs { get; set; }
    public List<ModReport> Mods { get; set; } = new();
}

public class ModReport
{
    public string Name { get; set; } = "";
    public bool IsValid { get; set; }
    public int TotalFiles { get; set; }
    public int ValidFiles { get; set; }
    public List<string> Errors { get; set; } = new();
}

public class InGameReport
{
    public bool Success { get; set; }
    public bool Skipped { get; set; }
    public string? SkipReason { get; set; }
    public int Total { get; set; }
    public int Passed { get; set; }
    public int Failed { get; set; }
    public double DurationSeconds { get; set; }
}

public class LogReport
{
    public bool Success { get; set; }
    public bool Skipped { get; set; }
    public string? SkipReason { get; set; }
    public int TotalEntries { get; set; }
    public int ErrorCount { get; set; }
    public int WarningCount { get; set; }
    public List<string> PassedChecks { get; set; } = new();
    public List<string> Failures { get; set; } = new();
}

/// <summary>
/// Generates JSON and HTML reports
/// </summary>
public class ReportGenerator
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
    
    /// <summary>
    /// Save report as JSON
    /// </summary>
    public void SaveJson(TestReport report, string outputPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");
        var json = JsonSerializer.Serialize(report, JsonOptions);
        File.WriteAllText(outputPath, json);
    }
    
    /// <summary>
    /// Save report as HTML
    /// </summary>
    public void SaveHtml(TestReport report, string outputPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");
        var html = GenerateHtml(report);
        File.WriteAllText(outputPath, html);
    }
    
    private string GenerateHtml(TestReport report)
    {
        var status = report.OverallSuccess ? "✓ PASSED" : "✗ FAILED";
        var statusColor = report.OverallSuccess ? "#22c55e" : "#ef4444";
        
        return $@"<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>HoboMod Test Report - {report.Timestamp:yyyy-MM-dd HH:mm}</title>
    <style>
        * {{ margin: 0; padding: 0; box-sizing: border-box; }}
        body {{ 
            font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
            background: linear-gradient(135deg, #1a1a2e 0%, #16213e 100%);
            color: #e2e8f0;
            min-height: 100vh;
            padding: 2rem;
        }}
        .container {{ max-width: 900px; margin: 0 auto; }}
        h1 {{ 
            font-size: 2.5rem; 
            margin-bottom: 0.5rem;
            background: linear-gradient(90deg, #06b6d4, #8b5cf6);
            -webkit-background-clip: text;
            -webkit-text-fill-color: transparent;
        }}
        .timestamp {{ color: #94a3b8; margin-bottom: 2rem; }}
        .status {{ 
            font-size: 1.5rem; 
            font-weight: bold; 
            color: {statusColor};
            padding: 1rem 2rem;
            background: rgba(255,255,255,0.05);
            border-radius: 0.5rem;
            display: inline-block;
            margin-bottom: 2rem;
        }}
        .card {{
            background: rgba(255,255,255,0.05);
            border-radius: 0.75rem;
            padding: 1.5rem;
            margin-bottom: 1rem;
            border: 1px solid rgba(255,255,255,0.1);
        }}
        .card h2 {{ 
            font-size: 1.25rem; 
            margin-bottom: 1rem;
            display: flex;
            align-items: center;
            gap: 0.5rem;
        }}
        .pass {{ color: #22c55e; }}
        .fail {{ color: #ef4444; }}
        .warn {{ color: #eab308; }}
        .skip {{ color: #94a3b8; }}
        .stat {{ display: flex; justify-content: space-between; padding: 0.5rem 0; border-bottom: 1px solid rgba(255,255,255,0.05); }}
        .stat:last-child {{ border-bottom: none; }}
        .stat-label {{ color: #94a3b8; }}
        .error-list {{ 
            background: rgba(239, 68, 68, 0.1); 
            border-radius: 0.5rem; 
            padding: 1rem; 
            margin-top: 1rem;
            font-family: monospace;
            font-size: 0.875rem;
        }}
        .error-list li {{ margin: 0.25rem 0; color: #fca5a5; }}
        .footer {{ text-align: center; margin-top: 2rem; color: #64748b; font-size: 0.875rem; }}
    </style>
</head>
<body>
    <div class=""container"">
        <h1>🏠 HoboMod Test Report</h1>
        <p class=""timestamp"">Generated: {report.Timestamp:yyyy-MM-dd HH:mm:ss} | Duration: {report.TotalDuration.TotalSeconds:F1}s</p>
        <div class=""status"">{status}</div>
        
        {GenerateBuildCard(report.Build)}
        {GenerateUnitTestCard(report.UnitTests)}
        {GenerateSchemaCard(report.Schemas)}
        {GenerateModValidationCard(report.ModValidation)}
        {GenerateInGameCard(report.InGameTests)}
        {GenerateLogCard(report.LogVerification)}
        
        <div class=""footer"">
            HoboMod Test Harness v{report.HarnessVersion}
        </div>
    </div>
</body>
</html>";
    }
    
    private string GenerateBuildCard(BuildReport? build)
    {
        if (build == null) return "";
        var icon = build.Success ? "✓" : "✗";
        var cls = build.Success ? "pass" : "fail";
        return $@"
        <div class=""card"">
            <h2><span class=""{cls}"">{icon}</span> Build Verification</h2>
            <div class=""stat""><span class=""stat-label"">Status</span><span class=""{cls}"">{(build.Success ? "Succeeded" : "Failed")}</span></div>
            <div class=""stat""><span class=""stat-label"">Duration</span><span>{build.DurationSeconds:F2}s</span></div>
            {(build.ErrorMessage != null ? $"<div class=\"error-list\">{build.ErrorMessage}</div>" : "")}
        </div>";
    }
    
    private string GenerateUnitTestCard(UnitTestReport? tests)
    {
        if (tests == null) return "";
        var icon = tests.Success ? "✓" : "✗";
        var cls = tests.Success ? "pass" : "fail";
        return $@"
        <div class=""card"">
            <h2><span class=""{cls}"">{icon}</span> Unit Tests</h2>
            <div class=""stat""><span class=""stat-label"">Total</span><span>{tests.Total}</span></div>
            <div class=""stat""><span class=""stat-label"">Passed</span><span class=""pass"">{tests.Passed}</span></div>
            <div class=""stat""><span class=""stat-label"">Failed</span><span class=""{(tests.Failed > 0 ? "fail" : "")}"">{tests.Failed}</span></div>
            <div class=""stat""><span class=""stat-label"">Duration</span><span>{tests.DurationSeconds:F2}s</span></div>
            {(tests.FailedTests.Count > 0 ? $"<ul class=\"error-list\">{string.Join("", tests.FailedTests.ConvertAll(t => $"<li>{t}</li>"))}</ul>" : "")}
        </div>";
    }
    
    private string GenerateSchemaCard(SchemaReport? schemas)
    {
        if (schemas == null) return "";
        return $@"
        <div class=""card"">
            <h2><span class=""pass"">✓</span> Schema Generation</h2>
            <div class=""stat""><span class=""stat-label"">Schemas Generated</span><span>{schemas.GeneratedSchemas.Count}</span></div>
        </div>";
    }
    
    private string GenerateModValidationCard(ModValidationReport? validation)
    {
        if (validation == null) return "";
        var icon = validation.Success ? "✓" : "✗";
        var cls = validation.Success ? "pass" : "fail";
        return $@"
        <div class=""card"">
            <h2><span class=""{cls}"">{icon}</span> Mod Validation</h2>
            <div class=""stat""><span class=""stat-label"">Total Mods</span><span>{validation.TotalMods}</span></div>
            <div class=""stat""><span class=""stat-label"">Valid</span><span class=""pass"">{validation.ValidMods}</span></div>
            <div class=""stat""><span class=""stat-label"">Invalid</span><span class=""{(validation.InvalidMods > 0 ? "fail" : "")}"">{validation.InvalidMods}</span></div>
        </div>";
    }
    
    private string GenerateInGameCard(InGameReport? inGame)
    {
        if (inGame == null) return "";
        if (inGame.Skipped)
        {
            return $@"
            <div class=""card"">
                <h2><span class=""skip"">⊘</span> In-Game Tests</h2>
                <div class=""stat""><span class=""stat-label"">Status</span><span class=""skip"">Skipped</span></div>
                <div class=""stat""><span class=""stat-label"">Reason</span><span>{inGame.SkipReason}</span></div>
            </div>";
        }
        var icon = inGame.Success ? "✓" : "✗";
        var cls = inGame.Success ? "pass" : "fail";
        return $@"
        <div class=""card"">
            <h2><span class=""{cls}"">{icon}</span> In-Game Tests</h2>
            <div class=""stat""><span class=""stat-label"">Passed</span><span class=""pass"">{inGame.Passed}</span></div>
            <div class=""stat""><span class=""stat-label"">Failed</span><span class=""{(inGame.Failed > 0 ? "fail" : "")}"">{inGame.Failed}</span></div>
            <div class=""stat""><span class=""stat-label"">Duration</span><span>{inGame.DurationSeconds:F2}s</span></div>
        </div>";
    }
    
    private string GenerateLogCard(LogReport? log)
    {
        if (log == null) return "";
        if (log.Skipped)
        {
            return $@"
            <div class=""card"">
                <h2><span class=""skip"">⊘</span> Log Verification</h2>
                <div class=""stat""><span class=""stat-label"">Status</span><span class=""skip"">Skipped</span></div>
                <div class=""stat""><span class=""stat-label"">Reason</span><span>{log.SkipReason}</span></div>
            </div>";
        }
        var icon = log.Success ? "✓" : "✗";
        var cls = log.Success ? "pass" : "fail";
        return $@"
        <div class=""card"">
            <h2><span class=""{cls}"">{icon}</span> Log Verification</h2>
            <div class=""stat""><span class=""stat-label"">Total Entries</span><span>{log.TotalEntries}</span></div>
            <div class=""stat""><span class=""stat-label"">Errors</span><span class=""{(log.ErrorCount > 0 ? "warn" : "")}"">{log.ErrorCount}</span></div>
            <div class=""stat""><span class=""stat-label"">Warnings</span><span>{log.WarningCount}</span></div>
            {(log.Failures.Count > 0 ? $"<ul class=\"error-list\">{string.Join("", log.Failures.ConvertAll(f => $"<li>{f}</li>"))}</ul>" : "")}
        </div>";
    }
}
