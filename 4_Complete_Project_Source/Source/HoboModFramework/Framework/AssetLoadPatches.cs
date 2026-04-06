using System;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace HoboModPlugin.Framework
{
    /// <summary>
    /// Harmony patches for intercepting Unity asset loading.
    /// Enables the Universal Asset Override System to replace any game asset.
    /// 
    /// NOTE: Uses manual patching and reflection to find IL2CPP method signatures.
    /// </summary>
    public static class AssetLoadPatches
    {
        private static ManualLogSource _log;
        private static bool _enabled = true;
        private static Harmony _harmony;
        
        /// <summary>
        /// Initialize and apply patches manually
        /// </summary>
        public static void Initialize(ManualLogSource log)
        {
            _log = log;
            _harmony = new Harmony("com.hobomod.assetpatches");
            
            // Log all methods on Resources to understand IL2CPP naming
            LogResourcesMethods();
            
            ApplyPatches();
            

        }
        
        /// <summary>
        /// Debug: List all methods on UnityEngine.Resources to find correct names
        /// </summary>
        private static void LogResourcesMethods()
        {
            if (!Plugin.EnableDebugMode.Value) return;
            
            _log?.LogDebug("[AssetLoadPatches] Scanning Resources class methods...");
            
            var methods = typeof(Resources).GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            
            foreach (var method in methods)
            {
                if (method.Name.Contains("Load"))
                {
                    var parameters = method.GetParameters();
                    var paramStr = string.Join(", ", Array.ConvertAll(parameters, p => $"{p.ParameterType.Name} {p.Name}"));
                    _log?.LogDebug($"  Found: {method.Name}({paramStr}) -> {method.ReturnType.Name}");
                }
            }
        }
        
        /// <summary>
        /// Apply patches manually to avoid ambiguous method issues
        /// </summary>
        private static void ApplyPatches()
        {
            var prefixMethod = typeof(AssetLoadPatches).GetMethod(nameof(Resources_Load_Prefix), 
                BindingFlags.Static | BindingFlags.NonPublic);
            var postfixMethod = typeof(AssetLoadPatches).GetMethod(nameof(Resources_Load_Postfix), 
                BindingFlags.Static | BindingFlags.NonPublic);
            
            if (prefixMethod == null)
            {
                _log?.LogError("[AssetLoadPatches] Could not find prefix method");
                return;
            }
            
            // Try different method signatures that might exist in IL2CPP
            string[] methodNames = { "Load", "Load_Internal", "LoadInternal" };
            
            foreach (var methodName in methodNames)
            {
                var allMethods = typeof(Resources).GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                
                foreach (var method in allMethods)
                {
                    if (method.Name == methodName && !method.IsGenericMethod)
                    {
                        var parameters = method.GetParameters();
                        
                        if (parameters.Length >= 1 && parameters[0].ParameterType == typeof(string))
                        {
                            try
                            {
                                _harmony.Patch(method, 
                                    prefix: new HarmonyMethod(prefixMethod),
                                    postfix: postfixMethod != null ? new HarmonyMethod(postfixMethod) : null);
                                var paramStr = string.Join(", ", Array.ConvertAll(parameters, p => p.ParameterType.Name));
                                if (Plugin.EnableDebugMode.Value)
                                    _log?.LogDebug($"[AssetLoadPatches] Patched {methodName}({paramStr})");
                                return; // Patched one successfully
                            }
                            catch (Exception ex)
                            {
                                _log?.LogWarning($"[AssetLoadPatches] Failed to patch {methodName}: {ex.Message}");
                            }
                        }
                    }
                }
            }
            
            _log?.LogWarning("[AssetLoadPatches] No suitable Resources.Load method found to patch");
        }
        
        /// <summary>
        /// Enable or disable asset override interception
        /// </summary>
        public static void SetEnabled(bool enabled)
        {
            _enabled = enabled;
            _log?.LogInfo($"[AssetLoadPatches] Enabled = {enabled}");
        }
        
        // ==================== Patch Methods ====================
        
        /// <summary>
        /// Generic prefix that works with any Load signature
        /// </summary>
        private static bool Resources_Load_Prefix(string path, ref UnityEngine.Object __result)
        {
            if (!_enabled) return true;
            if (string.IsNullOrEmpty(path)) return true;
            
            if (AssetOverrideRegistry.HasOverride(path))
            {
                try
                {
                    var overrideAsset = AssetOverrideRegistry.LoadOverride<UnityEngine.Object>(path);
                    
                    if (overrideAsset != null)
                    {
                        __result = overrideAsset;
                        _log?.LogInfo($"[AssetLoadPatches] OVERRIDE APPLIED: {path} -> {overrideAsset.name}");
                        return false; // Skip original
                    }
                }
                catch (Exception ex)
                {
                    _log?.LogError($"[AssetLoadPatches] Error overriding {path}: {ex.Message}");
                }
            }
            
            return true; // Run original
        }
        
        /// <summary>
        /// Postfix to log all asset loads for discovery purposes
        /// </summary>
        private static void Resources_Load_Postfix(string path, UnityEngine.Object __result)
        {
            // Only log if verbose logging is enabled (off by default)
            if (_logAllLoads && __result != null)
            {
                _log?.LogInfo($"[AssetLoad] Resources.Load(\"{path}\") -> {__result.GetType().Name}");
            }
        }
        
        // ==================== Debug Helpers ====================
        
        private static bool _logAllLoads = false; // Disabled by default, enable with SetVerboseLogging(true)
        
        public static void SetVerboseLogging(bool enabled)
        {
            _logAllLoads = enabled;
            _log?.LogInfo($"[AssetLoadPatches] Verbose logging = {enabled}");
        }
    }
}
