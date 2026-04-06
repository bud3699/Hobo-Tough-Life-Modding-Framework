using System;
using System.Diagnostics;
using System.IO;
using HoboModTestHarness.Utils;

namespace HoboModTestHarness.Runners;

/// <summary>
/// Result of validating a C# plugin mod
/// </summary>
public class CSharpPluginResult
{
    public string ModPath { get; set; } = "";
    public string ModName { get; set; } = "";
    public string CsprojPath { get; set; } = "";
    public bool Builds { get; set; }
    public string Output { get; set; } = "";
    public string Errors { get; set; } = "";
    public TimeSpan Duration { get; set; }
    public int WarningCount { get; set; }
    public int ErrorCount { get; set; }
}

/// <summary>
/// Validates C# plugin mods by building them with dotnet
/// </summary>
public class CSharpPluginValidator
{
    /// <summary>
    /// Validate a C# plugin by building it
    /// </summary>
    public CSharpPluginResult Validate(ModInfo mod)
    {
        if (mod.Type != ModType.CSharpPlugin || string.IsNullOrEmpty(mod.CsprojPath))
        {
            return new CSharpPluginResult
            {
                ModPath = mod.Path,
                ModName = mod.Name,
                Builds = false,
                Errors = "Not a C# plugin or no .csproj found"
            };
        }
        
        return BuildProject(mod.CsprojPath, mod.Name, mod.Path);
    }
    
    /// <summary>
    /// Build a .csproj file and return results
    /// </summary>
    private CSharpPluginResult BuildProject(string csprojPath, string modName, string modPath)
    {
        var result = new CSharpPluginResult
        {
            ModPath = modPath,
            ModName = modName,
            CsprojPath = csprojPath
        };
        
        if (!File.Exists(csprojPath))
        {
            result.Builds = false;
            result.Errors = $"Project file not found: {csprojPath}";
            return result;
        }
        
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"build \"{csprojPath}\" -c Release -v q --nologo",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(csprojPath)
        };
        
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            using var process = new Process { StartInfo = startInfo };
            process.Start();
            
            result.Output = process.StandardOutput.ReadToEnd();
            result.Errors = process.StandardError.ReadToEnd();
            
            process.WaitForExit();
            stopwatch.Stop();
            
            result.Duration = stopwatch.Elapsed;
            result.Builds = process.ExitCode == 0;
            
            // Count warnings and errors from output
            result.WarningCount = CountOccurrences(result.Output, "warning");
            result.ErrorCount = CountOccurrences(result.Output, "error");
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            result.Builds = false;
            result.Errors = $"Failed to run build: {ex.Message}";
            result.Duration = stopwatch.Elapsed;
        }
        
        return result;
    }
    
    private int CountOccurrences(string text, string pattern)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        
        int count = 0;
        int index = 0;
        while ((index = text.IndexOf(pattern, index, StringComparison.OrdinalIgnoreCase)) != -1)
        {
            count++;
            index += pattern.Length;
        }
        return count;
    }
}
