using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace HoboModTestHarness.Utils;

/// <summary>
/// Types of mods that can be detected
/// Add new types here as needed - the system is extensible
/// </summary>
public enum ModType
{
    /// <summary>JSON-based mod with mod.json</summary>
    JsonMod,
    
    /// <summary>C# BepInEx plugin with .csproj</summary>
    CSharpPlugin,
    
    /// <summary>Has content folders but missing required mod.json</summary>
    IncompleteJsonMod,
    
    /// <summary>Compiled DLL only (no source)</summary>
    CompiledPlugin,
    
    /// <summary>Cannot determine mod type</summary>
    Unknown
}

/// <summary>
/// Information about a detected mod
/// </summary>
public class ModInfo
{
    public string Path { get; set; } = "";
    public string Name { get; set; } = "";
    public ModType Type { get; set; }
    
    /// <summary>Path to .csproj file (for CSharpPlugin type)</summary>
    public string? CsprojPath { get; set; }
    
    /// <summary>Path to mod.json (for JsonMod type)</summary>
    public string? ModJsonPath { get; set; }
    
    /// <summary>Paths to DLL files (for CompiledPlugin type)</summary>
    public List<string> DllPaths { get; set; } = new();
    
    /// <summary>What content folders exist (items/, recipes/, quests/)</summary>
    public List<string> ContentFolders { get; set; } = new();
    
    /// <summary>Human-readable type name</summary>
    public string TypeName => Type switch
    {
        ModType.JsonMod => "JSON Mod",
        ModType.CSharpPlugin => "C# Plugin",
        ModType.IncompleteJsonMod => "Incomplete",
        ModType.CompiledPlugin => "Compiled DLL",
        ModType.Unknown => "Unknown",
        _ => "Unknown"
    };
}

/// <summary>
/// Detects the type of mod in a folder.
/// 
/// Detection priority (first match wins):
/// 1. Has .csproj → CSharpPlugin
/// 2. Has mod.json → JsonMod
/// 3. Has only .dll files → CompiledPlugin
/// 4. Has items/, recipes/, or quests/ but no mod.json → IncompleteJsonMod
/// 5. Nothing recognized → Unknown
/// 
/// To add new mod types:
/// 1. Add to ModType enum
/// 2. Add detection logic in Detect() method
/// 3. Add TypeName in ModInfo
/// </summary>
public class ModTypeDetector
{
    private static readonly string[] ContentFolderNames = { "items", "recipes", "quests" };
    
    /// <summary>
    /// Detect the type of mod in a folder
    /// </summary>
    public ModInfo Detect(string folderPath)
    {
        var info = new ModInfo
        {
            Path = folderPath,
            Name = Path.GetFileName(folderPath)
        };
        
        if (!Directory.Exists(folderPath))
        {
            info.Type = ModType.Unknown;
            return info;
        }
        
        // Check for content folders
        foreach (var contentFolder in ContentFolderNames)
        {
            var contentPath = Path.Combine(folderPath, contentFolder);
            if (Directory.Exists(contentPath))
            {
                info.ContentFolders.Add(contentFolder);
            }
        }
        
        // Check for .csproj files
        var csprojFiles = Directory.GetFiles(folderPath, "*.csproj", SearchOption.TopDirectoryOnly);
        if (csprojFiles.Length > 0)
        {
            info.Type = ModType.CSharpPlugin;
            info.CsprojPath = csprojFiles[0];
            return info;
        }
        
        // Check for mod.json
        var modJsonPath = Path.Combine(folderPath, "mod.json");
        if (File.Exists(modJsonPath))
        {
            info.Type = ModType.JsonMod;
            info.ModJsonPath = modJsonPath;
            return info;
        }
        
        // Check for DLL files only (compiled plugin without source)
        var dllFiles = Directory.GetFiles(folderPath, "*.dll", SearchOption.TopDirectoryOnly);
        if (dllFiles.Length > 0)
        {
            info.Type = ModType.CompiledPlugin;
            info.DllPaths = dllFiles.ToList();
            return info;
        }
        
        // Has content folders but no mod.json = incomplete JSON mod
        if (info.ContentFolders.Any())
        {
            info.Type = ModType.IncompleteJsonMod;
            return info;
        }
        
        // Nothing recognized
        info.Type = ModType.Unknown;
        return info;
    }
    
    /// <summary>
    /// Scan a directory for all mods and detect their types
    /// </summary>
    public List<ModInfo> ScanDirectory(string modsPath)
    {
        var results = new List<ModInfo>();
        
        if (!Directory.Exists(modsPath))
        {
            return results;
        }
        
        var folders = Directory.GetDirectories(modsPath);
        
        foreach (var folder in folders)
        {
            // Skip folders starting with underscore (examples) or dot (hidden)
            var folderName = Path.GetFileName(folder);
            if (folderName.StartsWith("_") || folderName.StartsWith("."))
            {
                continue;
            }
            
            var info = Detect(folder);
            results.Add(info);
        }
        
        return results;
    }
    
    /// <summary>
    /// Get a summary of mod types in a directory
    /// </summary>
    public Dictionary<ModType, int> GetTypeSummary(string modsPath)
    {
        var mods = ScanDirectory(modsPath);
        return mods.GroupBy(m => m.Type)
                   .ToDictionary(g => g.Key, g => g.Count());
    }
}
