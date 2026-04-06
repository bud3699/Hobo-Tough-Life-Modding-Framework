using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HoboModTestHarness.Utils;

namespace HoboModTestHarness.Runners;

/// <summary>
/// What we expect to see in the log
/// </summary>
public class LogExpectations
{
    /// <summary>
    /// Mod IDs that should be loaded
    /// </summary>
    public List<string> ExpectedMods { get; set; } = new();
    
    /// <summary>
    /// Item IDs that should be injected (e.g., "testsuite:super_apple")
    /// </summary>
    public List<string> ExpectedItems { get; set; } = new();
    
    /// <summary>
    /// Recipe IDs that should be registered
    /// </summary>
    public List<string> ExpectedRecipes { get; set; } = new();
    
    /// <summary>
    /// Events that should be registered
    /// </summary>
    public List<string> ExpectedEvents { get; set; } = new();
    
    /// <summary>
    /// Messages that should NOT appear (errors to flag)
    /// </summary>
    public List<string> ForbiddenMessages { get; set; } = new();
    
    /// <summary>
    /// Whether any Error-severity entries should fail verification
    /// </summary>
    public bool FailOnErrors { get; set; } = true;
    
    /// <summary>
    /// Sources to ignore errors from (e.g., unrelated plugins)
    /// </summary>
    public List<string> IgnoreErrorsFromSources { get; set; } = new();
}

/// <summary>
/// Result of log verification
/// </summary>
public class LogVerificationResult
{
    public bool Success { get; set; }
    public string LogPath { get; set; } = "";
    public int TotalEntries { get; set; }
    public int ErrorCount { get; set; }
    public int WarningCount { get; set; }
    public List<string> Failures { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public List<string> PassedChecks { get; set; } = new();
    
    // Detailed findings
    public List<string> LoadedMods { get; set; } = new();
    public List<string> InjectedItems { get; set; } = new();
    public List<string> RegisteredRecipes { get; set; } = new();
    public List<LogEntry> Errors { get; set; } = new();
}

/// <summary>
/// Verifies BepInEx logs against expectations
/// </summary>
public class LogVerifier
{
    private readonly BepInExLogParser _parser = new();
    
    /// <summary>
    /// Verify a log file against expectations
    /// </summary>
    public LogVerificationResult Verify(string logPath, LogExpectations expectations)
    {
        var result = new LogVerificationResult
        {
            LogPath = logPath
        };
        
        // Check if log exists
        if (!File.Exists(logPath))
        {
            result.Success = false;
            result.Failures.Add($"Log file not found: {logPath}");
            return result;
        }
        
        // Parse log
        BepInExLog log;
        try
        {
            log = _parser.Parse(logPath);
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Failures.Add($"Failed to parse log: {ex.Message}");
            return result;
        }
        
        result.TotalEntries = log.Entries.Count;
        
        // Check for errors
        var errors = log.Errors
            .Where(e => !expectations.IgnoreErrorsFromSources
                .Any(src => e.Source.Contains(src, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        
        result.ErrorCount = errors.Count;
        result.Errors = errors;
        
        if (expectations.FailOnErrors && errors.Any())
        {
            result.Failures.Add($"Found {errors.Count} error(s) in log:");
            foreach (var error in errors.Take(10))
            {
                result.Failures.Add($"  [{error.Source}] {error.Message}");
            }
            if (errors.Count > 10)
            {
                result.Failures.Add($"  ... and {errors.Count - 10} more");
            }
        }
        
        // Count warnings
        result.WarningCount = log.Warnings.Count();
        
        // Check expected mods loaded
        foreach (var expectedMod in expectations.ExpectedMods)
        {
            var loaded = log.ContainsMessage($"Loaded mod: {expectedMod}") ||
                         log.ContainsMessage($"Loading mod: {expectedMod}") ||
                         log.ContainsMessage($"[{expectedMod}]") ||
                         log.FromSource("HoboModPlugin").Any(e => 
                             e.Message.Contains(expectedMod, StringComparison.OrdinalIgnoreCase));
            
            if (loaded)
            {
                result.LoadedMods.Add(expectedMod);
                result.PassedChecks.Add($"Mod loaded: {expectedMod}");
            }
            else
            {
                result.Failures.Add($"Mod not loaded: {expectedMod}");
            }
        }
        
        // Check expected items injected
        foreach (var expectedItem in expectations.ExpectedItems)
        {
            var injected = log.ContainsMessage($"Injected item: {expectedItem}") ||
                          log.ContainsMessage($"Registered item: {expectedItem}") ||
                          log.ContainsMessage($"item '{expectedItem}'") ||
                          log.ContainsMessage(expectedItem);
            
            if (injected)
            {
                result.InjectedItems.Add(expectedItem);
                result.PassedChecks.Add($"Item injected: {expectedItem}");
            }
            else
            {
                result.Warnings.Add($"Item not found in log (may still work): {expectedItem}");
            }
        }
        
        // Check expected recipes
        foreach (var expectedRecipe in expectations.ExpectedRecipes)
        {
            var registered = log.ContainsMessage($"Registered recipe: {expectedRecipe}") ||
                            log.ContainsMessage($"recipe '{expectedRecipe}'") ||
                            log.ContainsMessage(expectedRecipe);
            
            if (registered)
            {
                result.RegisteredRecipes.Add(expectedRecipe);
                result.PassedChecks.Add($"Recipe registered: {expectedRecipe}");
            }
            else
            {
                result.Warnings.Add($"Recipe not found in log: {expectedRecipe}");
            }
        }
        
        // Check expected events
        foreach (var expectedEvent in expectations.ExpectedEvents)
        {
            var registered = log.ContainsMessage($"Registered event: {expectedEvent}") ||
                            log.ContainsMessage($"Event handler: {expectedEvent}") ||
                            log.ContainsMessage(expectedEvent);
            
            if (registered)
            {
                result.PassedChecks.Add($"Event registered: {expectedEvent}");
            }
            else
            {
                result.Warnings.Add($"Event not found in log: {expectedEvent}");
            }
        }
        
        // Check forbidden messages
        foreach (var forbidden in expectations.ForbiddenMessages)
        {
            if (log.ContainsMessage(forbidden))
            {
                result.Failures.Add($"Found forbidden message: {forbidden}");
            }
        }
        
        result.Success = result.Failures.Count == 0;
        return result;
    }
    
    /// <summary>
    /// Quick check for any errors in log
    /// </summary>
    public (bool HasErrors, int ErrorCount, List<string> TopErrors) QuickErrorCheck(string logPath)
    {
        if (!File.Exists(logPath))
        {
            return (false, 0, new List<string>());
        }
        
        var log = _parser.Parse(logPath);
        var errors = log.Errors.ToList();
        
        return (
            errors.Any(),
            errors.Count,
            errors.Take(5).Select(e => $"[{e.Source}] {e.Message}").ToList()
        );
    }
    
    /// <summary>
    /// Get a summary of the log
    /// </summary>
    public LogSummary GetSummary(string logPath)
    {
        var summary = new LogSummary { LogPath = logPath };
        
        if (!File.Exists(logPath))
        {
            summary.Exists = false;
            return summary;
        }
        
        summary.Exists = true;
        
        var log = _parser.Parse(logPath);
        summary.TotalEntries = log.Entries.Count;
        summary.ErrorCount = log.Errors.Count();
        summary.WarningCount = log.Warnings.Count();
        summary.LogDate = log.LogDate;
        
        // Get unique sources
        summary.Sources = log.Entries
            .Where(e => !string.IsNullOrEmpty(e.Source))
            .Select(e => e.Source)
            .Distinct()
            .OrderBy(s => s)
            .ToList();
        
        // Check for HoboMod entries
        summary.HoboModEntries = log.FromSource("HoboMod").Count();
        
        return summary;
    }
}

/// <summary>
/// Summary of a BepInEx log
/// </summary>
public class LogSummary
{
    public string LogPath { get; set; } = "";
    public bool Exists { get; set; }
    public DateTime? LogDate { get; set; }
    public int TotalEntries { get; set; }
    public int ErrorCount { get; set; }
    public int WarningCount { get; set; }
    public int HoboModEntries { get; set; }
    public List<string> Sources { get; set; } = new();
}
