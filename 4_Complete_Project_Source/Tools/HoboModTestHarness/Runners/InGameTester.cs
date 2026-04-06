using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

namespace HoboModTestHarness.Runners;

/// <summary>
/// Result of in-game test execution via Surity
/// </summary>
public class InGameTestResult
{
    public bool Success { get; set; }
    public int TotalTests { get; set; }
    public int Passed { get; set; }
    public int Failed { get; set; }
    public TimeSpan Duration { get; set; }
    public string Output { get; set; } = "";
    public string Errors { get; set; } = "";
    public List<string> FailedTests { get; set; } = new();
    public bool GameLaunched { get; set; }
    public bool SurityInstalled { get; set; }
}

/// <summary>
/// Runs in-game tests using Surity CLI
/// </summary>
public class InGameTester
{
    private readonly string _gamePath;
    private readonly string _gameExe;
    
    public InGameTester(string gamePath)
    {
        _gamePath = gamePath;
        _gameExe = Path.Combine(gamePath, "HoboRPG.exe");
    }
    
    /// <summary>
    /// Check if Surity CLI is installed as a dotnet tool
    /// </summary>
    public bool IsSurityInstalled()
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = "tool list -g",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            
            using var process = new Process { StartInfo = startInfo };
            process.Start();
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            
            return output.Contains("surity", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
    
    /// <summary>
    /// Install Surity CLI as a global dotnet tool
    /// </summary>
    public bool InstallSurity()
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = "tool install -g Surity.CLI",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            
            using var process = new Process { StartInfo = startInfo };
            process.Start();
            process.WaitForExit();
            
            return process.ExitCode == 0 || IsSurityInstalled();
        }
        catch
        {
            return false;
        }
    }
    
    /// <summary>
    /// Run in-game tests using Surity
    /// </summary>
    public InGameTestResult Run(int timeoutSeconds = 120)
    {
        var result = new InGameTestResult();
        var stopwatch = Stopwatch.StartNew();
        
        // Check prerequisites
        if (!File.Exists(_gameExe))
        {
            result.Errors = $"Game executable not found: {_gameExe}";
            result.Success = false;
            return result;
        }
        
        // Check if Surity is installed
        result.SurityInstalled = IsSurityInstalled();
        if (!result.SurityInstalled)
        {
            // Try to install it
            Console.WriteLine("Surity CLI not found, installing...");
            if (!InstallSurity())
            {
                result.Errors = "Failed to install Surity CLI. Run: dotnet tool install -g Surity.CLI";
                result.Success = false;
                return result;
            }
            result.SurityInstalled = true;
        }
        
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"surity \"{_gameExe}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = _gamePath
            };
            
            using var process = new Process { StartInfo = startInfo };
            process.Start();
            
            // Wait for completion with timeout
            if (!process.WaitForExit(timeoutSeconds * 1000))
            {
                process.Kill();
                result.Errors = $"Test execution timed out after {timeoutSeconds}s";
                result.Success = false;
                stopwatch.Stop();
                result.Duration = stopwatch.Elapsed;
                return result;
            }
            
            result.Output = process.StandardOutput.ReadToEnd();
            result.Errors = process.StandardError.ReadToEnd();
            result.GameLaunched = true;
            
            stopwatch.Stop();
            result.Duration = stopwatch.Elapsed;
            
            // Parse Surity output
            ParseSurityOutput(result);
            
            result.Success = process.ExitCode == 0 && result.Failed == 0;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            result.Duration = stopwatch.Elapsed;
            result.Errors = $"Failed to run Surity: {ex.Message}";
            result.Success = false;
        }
        
        return result;
    }
    
    private void ParseSurityOutput(InGameTestResult result)
    {
        var output = result.Output;
        
        // Surity output format varies, but typically shows:
        // - Test count
        // - Pass/Fail for each test
        // - Summary at end
        
        // Count passed tests
        var passedMatches = Regex.Matches(output, @"✓|PASS|passed", RegexOptions.IgnoreCase);
        result.Passed = passedMatches.Count;
        
        // Count failed tests
        var failedMatches = Regex.Matches(output, @"✗|FAIL|failed", RegexOptions.IgnoreCase);
        result.Failed = failedMatches.Count;
        
        // Try to parse total from summary
        var totalMatch = Regex.Match(output, @"(\d+)\s+tests?", RegexOptions.IgnoreCase);
        if (totalMatch.Success)
        {
            result.TotalTests = int.Parse(totalMatch.Groups[1].Value);
        }
        else
        {
            result.TotalTests = result.Passed + result.Failed;
        }
        
        // Extract failed test names
        var failedTestMatches = Regex.Matches(output, @"(?:FAIL|✗)\s+(.+?)(?:\n|$)", RegexOptions.IgnoreCase);
        foreach (Match match in failedTestMatches)
        {
            result.FailedTests.Add(match.Groups[1].Value.Trim());
        }
    }
    
    /// <summary>
    /// Check if game path is valid and BepInEx is installed
    /// </summary>
    public (bool Valid, string Message) ValidateGameSetup()
    {
        if (!Directory.Exists(_gamePath))
        {
            return (false, $"Game path does not exist: {_gamePath}");
        }
        
        if (!File.Exists(_gameExe))
        {
            return (false, $"Game executable not found: {_gameExe}");
        }
        
        var bepInExPath = Path.Combine(_gamePath, "BepInEx");
        if (!Directory.Exists(bepInExPath))
        {
            return (false, "BepInEx is not installed in the game folder");
        }
        
        var pluginsPath = Path.Combine(bepInExPath, "plugins");
        if (!Directory.Exists(pluginsPath))
        {
            return (false, "BepInEx/plugins folder not found");
        }
        
        // Check if HoboModPlugin is installed
        var modPluginDll = Path.Combine(pluginsPath, "HoboModPlugin.dll");
        if (!File.Exists(modPluginDll))
        {
            return (false, "HoboModPlugin.dll not found in BepInEx/plugins");
        }
        
        return (true, "Game setup is valid for in-game testing");
    }
}
