using System;
using System.Diagnostics;
using System.IO;

namespace HoboModTestHarness.Runners;

public class BuildResult
{
    public bool Success { get; set; }
    public string Output { get; set; } = string.Empty;
    public string Errors { get; set; } = string.Empty;
    public TimeSpan Duration { get; set; }
}

public class BuildRunner
{
    public BuildResult Run(string projectPath)
    {
        if (string.IsNullOrWhiteSpace(projectPath))
        {
            throw new ArgumentNullException(nameof(projectPath));
        }

        if (!File.Exists(projectPath))
        {
            return new BuildResult 
            { 
                Success = false, 
                Errors = $"Project file not found: {projectPath}" 
            };
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"build \"{projectPath}\" -c Release",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        var stopwatch = Stopwatch.StartNew();
        
        using var process = new Process { StartInfo = startInfo };
        
        try 
        {
            process.Start();
            
            // Read output synchronously
            string output = process.StandardOutput.ReadToEnd();
            string errors = process.StandardError.ReadToEnd();
            
            process.WaitForExit();
            stopwatch.Stop();

            return new BuildResult
            {
                Success = process.ExitCode == 0,
                Output = output,
                Errors = errors,
                Duration = stopwatch.Elapsed
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return new BuildResult
            {
                Success = false,
                Errors = $"Exception running build: {ex.Message}",
                Duration = stopwatch.Elapsed
            };
        }
    }
}
