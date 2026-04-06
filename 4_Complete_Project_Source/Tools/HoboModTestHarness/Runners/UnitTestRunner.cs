using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

namespace HoboModTestHarness.Runners;

/// <summary>
/// Result of running unit tests
/// </summary>
public class UnitTestResult
{
    public bool Success { get; set; }
    public int TotalTests { get; set; }
    public int Passed { get; set; }
    public int Failed { get; set; }
    public int Skipped { get; set; }
    public TimeSpan Duration { get; set; }
    public string Output { get; set; } = "";
    public string Errors { get; set; } = "";
    public List<string> FailedTests { get; set; } = new();
}

/// <summary>
/// Runs NUnit tests via dotnet test
/// </summary>
public class UnitTestRunner
{
    /// <summary>
    /// Run all tests in the specified test project
    /// </summary>
    public UnitTestResult Run(string testProjectPath)
    {
        var result = new UnitTestResult();
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"test \"{testProjectPath}\" --no-restore --verbosity normal",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(testProjectPath) ?? ""
            };
            
            using var process = new Process { StartInfo = startInfo };
            process.Start();
            
            result.Output = process.StandardOutput.ReadToEnd();
            result.Errors = process.StandardError.ReadToEnd();
            
            process.WaitForExit();
            stopwatch.Stop();
            
            result.Duration = stopwatch.Elapsed;
            result.Success = process.ExitCode == 0;
            
            // Parse test results from output
            ParseTestResults(result);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            result.Duration = stopwatch.Elapsed;
            result.Success = false;
            result.Errors = $"Failed to run tests: {ex.Message}";
        }
        
        return result;
    }
    
    private void ParseTestResults(UnitTestResult result)
    {
        var output = result.Output;
        
        // Parse: "Passed: X, Failed: Y, Skipped: Z, Total: N"
        // Or: "Total tests: N"
        var summaryMatch = Regex.Match(output, 
            @"Passed:\s*(\d+).*?Failed:\s*(\d+).*?Skipped:\s*(\d+).*?Total:\s*(\d+)",
            RegexOptions.IgnoreCase);
        
        if (summaryMatch.Success)
        {
            result.Passed = int.Parse(summaryMatch.Groups[1].Value);
            result.Failed = int.Parse(summaryMatch.Groups[2].Value);
            result.Skipped = int.Parse(summaryMatch.Groups[3].Value);
            result.TotalTests = int.Parse(summaryMatch.Groups[4].Value);
        }
        else
        {
            // Try alternative format
            var totalMatch = Regex.Match(output, @"Total tests:\s*(\d+)", RegexOptions.IgnoreCase);
            if (totalMatch.Success)
            {
                result.TotalTests = int.Parse(totalMatch.Groups[1].Value);
            }
            
            var passedMatch = Regex.Match(output, @"Passed:\s*(\d+)", RegexOptions.IgnoreCase);
            if (passedMatch.Success)
            {
                result.Passed = int.Parse(passedMatch.Groups[1].Value);
            }
            
            var failedMatch = Regex.Match(output, @"Failed:\s*(\d+)", RegexOptions.IgnoreCase);
            if (failedMatch.Success)
            {
                result.Failed = int.Parse(failedMatch.Groups[1].Value);
            }
        }
        
        // Extract failed test names
        var failedMatches = Regex.Matches(output, @"Failed\s+(\S+)", RegexOptions.IgnoreCase);
        foreach (Match match in failedMatches)
        {
            if (match.Groups[1].Value != "!")  // Skip "Failed!"
            {
                result.FailedTests.Add(match.Groups[1].Value);
            }
        }
        
        // Update success based on parsed results
        if (result.TotalTests > 0)
        {
            result.Success = result.Failed == 0;
        }
    }
}
