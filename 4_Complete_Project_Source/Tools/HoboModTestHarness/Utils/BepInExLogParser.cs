using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace HoboModTestHarness.Utils;

/// <summary>
/// A single log entry from BepInEx
/// </summary>
public class LogEntry
{
    public string Severity { get; set; } = "";
    public string Source { get; set; } = "";
    public string Message { get; set; } = "";
    public int LineNumber { get; set; }
    public string RawLine { get; set; } = "";
}

/// <summary>
/// Parsed BepInEx log file
/// </summary>
public class BepInExLog
{
    public string FilePath { get; set; } = "";
    public DateTime? LogDate { get; set; }
    public List<LogEntry> Entries { get; set; } = new();
    
    public IEnumerable<LogEntry> Errors => Entries.Where(e => 
        e.Severity.Equals("Error", StringComparison.OrdinalIgnoreCase));
    
    public IEnumerable<LogEntry> Warnings => Entries.Where(e => 
        e.Severity.Equals("Warning", StringComparison.OrdinalIgnoreCase));
    
    public IEnumerable<LogEntry> FromSource(string source) => 
        Entries.Where(e => e.Source.Contains(source, StringComparison.OrdinalIgnoreCase));
    
    public bool ContainsMessage(string text) => 
        Entries.Any(e => e.Message.Contains(text, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Parses BepInEx log files into structured data
/// Log format: [Severity : Source] Message
/// </summary>
public class BepInExLogParser
{
    // Regex for BepInEx log format: [Severity : Source] Message
    private static readonly Regex LogLineRegex = new(
        @"^\[(\w+)\s*:\s*([^\]]+)\]\s*(.*)$",
        RegexOptions.Compiled);
    
    // Regex for timestamp in log header
    private static readonly Regex DateRegex = new(
        @"(\d{4}-\d{2}-\d{2}\s+\d{2}:\d{2}:\d{2})",
        RegexOptions.Compiled);
    
    /// <summary>
    /// Parse a BepInEx log file
    /// </summary>
    public BepInExLog Parse(string logPath)
    {
        if (!File.Exists(logPath))
        {
            throw new FileNotFoundException($"Log file not found: {logPath}");
        }
        
        var log = new BepInExLog
        {
            FilePath = logPath
        };
        
        var lines = File.ReadAllLines(logPath);
        
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            
            // Try to extract date from early lines
            if (log.LogDate == null && i < 10)
            {
                var dateMatch = DateRegex.Match(line);
                if (dateMatch.Success && DateTime.TryParse(dateMatch.Groups[1].Value, out var date))
                {
                    log.LogDate = date;
                }
            }
            
            // Parse log entry
            var match = LogLineRegex.Match(line);
            if (match.Success)
            {
                log.Entries.Add(new LogEntry
                {
                    Severity = match.Groups[1].Value.Trim(),
                    Source = match.Groups[2].Value.Trim(),
                    Message = match.Groups[3].Value.Trim(),
                    LineNumber = i + 1,
                    RawLine = line
                });
            }
            else if (!string.IsNullOrWhiteSpace(line))
            {
                // Non-standard line - could be continuation or header
                // Add as Info with empty source
                log.Entries.Add(new LogEntry
                {
                    Severity = "Info",
                    Source = "",
                    Message = line.Trim(),
                    LineNumber = i + 1,
                    RawLine = line
                });
            }
        }
        
        return log;
    }
    
    /// <summary>
    /// Parse log content from string (for testing)
    /// </summary>
    public BepInExLog ParseContent(string content)
    {
        var log = new BepInExLog();
        var lines = content.Split('\n');
        
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            var match = LogLineRegex.Match(line);
            
            if (match.Success)
            {
                log.Entries.Add(new LogEntry
                {
                    Severity = match.Groups[1].Value.Trim(),
                    Source = match.Groups[2].Value.Trim(),
                    Message = match.Groups[3].Value.Trim(),
                    LineNumber = i + 1,
                    RawLine = line
                });
            }
        }
        
        return log;
    }
}
