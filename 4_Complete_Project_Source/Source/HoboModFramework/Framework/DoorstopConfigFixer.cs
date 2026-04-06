using System;
using System.IO;
using BepInEx.Logging;

namespace HoboModPlugin.Framework
{
    /// <summary>
    /// Fixes the doorstop_config.ini to prevent BepInEx from being disabled on game restart.
    /// 
    /// Problem: The game's Steam integration can crash and restart the game. When this happens,
    /// the game may set DOORSTOP_DISABLE environment variable which prevents BepInEx from loading.
    /// 
    /// Solution: Set ignore_disable_switch = true in doorstop_config.ini so BepInEx always loads.
    /// This runs once on startup and fixes the config if needed.
    /// </summary>
    public static class DoorstopConfigFixer
    {
        private static ManualLogSource _log;
        
        public static void Initialize(ManualLogSource log, string pluginPath)
        {
            _log = log;
            
            try
            {
                // Find doorstop_config.ini (should be in game root, 3 levels up from plugins folder)
                // pluginPath is like: .../BepInEx/plugins/
                var bepInExPath = Directory.GetParent(pluginPath)?.FullName; // BepInEx
                var gamePath = Directory.GetParent(bepInExPath ?? "")?.FullName; // Game root
                
                if (string.IsNullOrEmpty(gamePath))
                {
                    _log?.LogWarning("[DoorstopFixer] Could not determine game path");
                    return;
                }
                
                var configPath = Path.Combine(gamePath, "doorstop_config.ini");
                
                if (!File.Exists(configPath))
                {
                    _log?.LogDebug("[DoorstopFixer] doorstop_config.ini not found (this is okay for non-doorstop setups)");
                    return;
                }
                
                // Read config
                var content = File.ReadAllText(configPath);
                
                // Check if fix is needed
                if (content.Contains("ignore_disable_switch = false"))
                {
                    _log?.LogInfo("[DoorstopFixer] Applying fix: ignore_disable_switch = true");
                    
                    // Apply fix
                    content = content.Replace(
                        "ignore_disable_switch = false",
                        "ignore_disable_switch = true"
                    );
                    
                    File.WriteAllText(configPath, content);
                    _log?.LogInfo("[DoorstopFixer] Fixed! Console will persist after game restart next launch.");
                }
                else if (content.Contains("ignore_disable_switch = true"))
                {
                    _log?.LogDebug("[DoorstopFixer] Config already correct");
                }
                else
                {
                    _log?.LogDebug("[DoorstopFixer] ignore_disable_switch setting not found in config");
                }
            }
            catch (Exception ex)
            {
                _log?.LogWarning($"[DoorstopFixer] Could not check/fix config: {ex.Message}");
            }
        }
    }
}
