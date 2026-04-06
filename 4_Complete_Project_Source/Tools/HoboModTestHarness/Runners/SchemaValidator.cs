using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NJsonSchema;

namespace HoboModTestHarness.Runners;

/// <summary>
/// Validation result for a single file
/// </summary>
public class FileValidationResult
{
    public string FilePath { get; set; } = "";
    public string FileType { get; set; } = ""; // "mod", "item", "recipe", "quest"
    public bool IsValid { get; set; }
    public List<string> Errors { get; set; } = new();
}

/// <summary>
/// Validation result for an entire mod
/// </summary>
public class ModValidationResult
{
    public string ModPath { get; set; } = "";
    public string ModId { get; set; } = "";
    public bool IsValid => !FileResults.Any(f => !f.IsValid);
    public List<FileValidationResult> FileResults { get; set; } = new();
    
    public int TotalFiles => FileResults.Count;
    public int ValidFiles => FileResults.Count(f => f.IsValid);
    public int InvalidFiles => FileResults.Count(f => !f.IsValid);
}

/// <summary>
/// Overall schema validation result
/// </summary>
public class SchemaValidationResult
{
    public bool IsValid => !ModResults.Any(m => !m.IsValid);
    public List<ModValidationResult> ModResults { get; set; } = new();
    public TimeSpan Duration { get; set; }
    
    public int TotalMods => ModResults.Count;
    public int ValidMods => ModResults.Count(m => m.IsValid);
    public int InvalidMods => ModResults.Count(m => !m.IsValid);
    public int TotalFiles => ModResults.Sum(m => m.TotalFiles);
    public int TotalErrors => ModResults.Sum(m => m.FileResults.Sum(f => f.Errors.Count));
}

/// <summary>
/// Validates mod JSON files against schemas
/// </summary>
public class SchemaValidator
{
    private readonly string _schemasPath;
    private readonly Dictionary<string, JsonSchema> _schemas = new();
    private bool _schemasLoaded;

    public SchemaValidator(string schemasPath)
    {
        _schemasPath = schemasPath;
    }
    
    /// <summary>
    /// Load all schemas from the Schemas directory
    /// </summary>
    public void LoadSchemas()
    {
        if (_schemasLoaded) return;
        
        var schemaFiles = new Dictionary<string, string>
        {
            ["mod"] = Path.Combine(_schemasPath, "mod.schema.json"),
            ["item"] = Path.Combine(_schemasPath, "item.schema.json"),
            ["recipe"] = Path.Combine(_schemasPath, "recipe.schema.json"),
            ["quest"] = Path.Combine(_schemasPath, "quest.schema.json")
        };
        
        foreach (var (type, path) in schemaFiles)
        {
            if (File.Exists(path))
            {
                var schemaJson = File.ReadAllText(path);
                _schemas[type] = JsonSchema.FromJsonAsync(schemaJson).Result;
            }
            else
            {
                Console.WriteLine($"Warning: Schema not found: {path}");
            }
        }
        
        _schemasLoaded = true;
    }
    
    /// <summary>
    /// Validate all mods in a directory
    /// </summary>
    public SchemaValidationResult ValidateAllMods(string modsPath)
    {
        var startTime = DateTime.Now;
        var result = new SchemaValidationResult();
        
        LoadSchemas();
        
        if (!Directory.Exists(modsPath))
        {
            return result;
        }
        
        var modFolders = Directory.GetDirectories(modsPath);
        
        foreach (var modFolder in modFolders)
        {
            // Skip folders starting with underscore (example mods)
            if (Path.GetFileName(modFolder).StartsWith("_"))
                continue;
                
            var modResult = ValidateMod(modFolder);
            result.ModResults.Add(modResult);
        }
        
        result.Duration = DateTime.Now - startTime;
        return result;
    }
    
    /// <summary>
    /// Validate a single mod folder
    /// </summary>
    public ModValidationResult ValidateMod(string modPath)
    {
        var result = new ModValidationResult
        {
            ModPath = modPath,
            ModId = Path.GetFileName(modPath)
        };
        
        LoadSchemas();
        
        // Validate mod.json
        var modJsonPath = Path.Combine(modPath, "mod.json");
        if (File.Exists(modJsonPath))
        {
            var fileResult = ValidateFile(modJsonPath, "mod");
            result.FileResults.Add(fileResult);
            
            // Try to extract mod ID from mod.json
            try
            {
                var json = File.ReadAllText(modJsonPath);
                var doc = System.Text.Json.JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("id", out var idElement))
                {
                    result.ModId = idElement.GetString() ?? result.ModId;
                }
            }
            catch { /* Ignore parse errors, use folder name */ }
        }
        else
        {
            result.FileResults.Add(new FileValidationResult
            {
                FilePath = modJsonPath,
                FileType = "mod",
                IsValid = false,
                Errors = new List<string> { "mod.json not found" }
            });
        }
        
        // Validate items/*.json
        var itemsPath = Path.Combine(modPath, "items");
        if (Directory.Exists(itemsPath))
        {
            foreach (var itemFile in Directory.GetFiles(itemsPath, "*.json"))
            {
                result.FileResults.Add(ValidateFile(itemFile, "item"));
            }
        }
        
        // Validate recipes/*.json
        var recipesPath = Path.Combine(modPath, "recipes");
        if (Directory.Exists(recipesPath))
        {
            foreach (var recipeFile in Directory.GetFiles(recipesPath, "*.json"))
            {
                result.FileResults.Add(ValidateFile(recipeFile, "recipe"));
            }
        }
        
        // Validate quests/*.json
        var questsPath = Path.Combine(modPath, "quests");
        if (Directory.Exists(questsPath))
        {
            foreach (var questFile in Directory.GetFiles(questsPath, "*.json"))
            {
                result.FileResults.Add(ValidateFile(questFile, "quest"));
            }
        }
        
        return result;
    }
    
    /// <summary>
    /// Validate a single JSON file against its schema
    /// </summary>
    public FileValidationResult ValidateFile(string filePath, string schemaType)
    {
        var result = new FileValidationResult
        {
            FilePath = filePath,
            FileType = schemaType
        };
        
        if (!_schemas.TryGetValue(schemaType, out var schema))
        {
            result.IsValid = false;
            result.Errors.Add($"No schema loaded for type: {schemaType}");
            return result;
        }
        
        try
        {
            var json = File.ReadAllText(filePath);
            var errors = schema.Validate(json);
            
            result.IsValid = errors.Count == 0;
            
            foreach (var error in errors)
            {
                var errorMsg = FormatValidationError(error);
                result.Errors.Add(errorMsg);
            }
        }
        catch (Exception ex)
        {
            result.IsValid = false;
            result.Errors.Add($"Failed to read/parse file: {ex.Message}");
        }
        
        return result;
    }
    
    private string FormatValidationError(NJsonSchema.Validation.ValidationError error)
    {
        var path = error.Path ?? "(root)";
        var kind = error.Kind.ToString();
        var message = error.ToString();
        
        // Simplify the error message
        if (!string.IsNullOrEmpty(error.Property))
        {
            return $"[{path}] Property '{error.Property}': {kind}";
        }
        
        return $"[{path}] {kind}: {message}";
    }
}
