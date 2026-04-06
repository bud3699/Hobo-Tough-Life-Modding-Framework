using System;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;

namespace HoboModPlugin.Framework.Addressables
{
    /// <summary>
    /// Harmony patches for intercepting Addressables asset loading.
    /// This is Phase 2 of the Universal Asset Override System.
    /// 
    /// Strategy: Patch ResourceManager.ProvideResource (non-generic) which is the 
    /// actual entry point for all Addressables loads.
    /// </summary>
    public static class AddressablePatches
    {
        private static ManualLogSource _log;
        private static Harmony _harmony;
        private static int _discoveryLogCount = 0;
        private static bool _verboseLogging = false;
        
        public static void Initialize(ManualLogSource log)
        {
            _log = log;
            _harmony = new Harmony("com.hobomod.addressablepatches");
            
            _log?.LogInfo("[AddressablePatches] Initializing...");
            
            ApplyPatches();
            
            _log?.LogInfo("[AddressablePatches] Initialized");
        }
        
        private static void ApplyPatches()
        {
            try
            {
                // Strategy: Patch ResourceManager.ProvideResource - the actual load entry point
                var resourceManagerType = typeof(ResourceManager);
                
                _log?.LogInfo("[AddressablePatches] Scanning ResourceManager methods...");
                
                var methods = resourceManagerType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                int patchedCount = 0;
                
                foreach (var method in methods)
                {
                    // Look for ProvideResource (non-generic version)
                    if (method.Name == "ProvideResource" && !method.IsGenericMethod)
                    {
                        var parameters = method.GetParameters();
                        var paramStr = string.Join(", ", Array.ConvertAll(parameters, p => $"{p.ParameterType.Name}"));
                        _log?.LogInfo($"  Found: {method.Name}({paramStr})");
                        
                        // Patch the version that takes IResourceLocation
                        if (parameters.Length >= 1 && 
                            typeof(IResourceLocation).IsAssignableFrom(parameters[0].ParameterType))
                        {
                            TryPatchProvideResource(method);
                            patchedCount++;
                        }
                    }
                    
                    // Also look for TransformInternalId - this is where we can redirect paths
                    if (method.Name == "TransformInternalId")
                    {
                        var parameters = method.GetParameters();
                        var paramStr = string.Join(", ", Array.ConvertAll(parameters, p => $"{p.ParameterType.Name}"));
                        _log?.LogInfo($"  Found: {method.Name}({paramStr})");
                        TryPatchTransformInternalId(method);
                        patchedCount++;
                    }
                }
                
                _log?.LogInfo($"[AddressablePatches] Patched {patchedCount} method(s)");
            }
            catch (Exception ex)
            {
                _log?.LogError($"[AddressablePatches] Error during patching: {ex.Message}");
                _log?.LogError(ex.StackTrace);
            }
        }
        
        private static void TryPatchProvideResource(MethodInfo targetMethod)
        {
            try
            {
                var prefix = typeof(AddressablePatches).GetMethod(
                    nameof(ProvideResource_Prefix), 
                    BindingFlags.Static | BindingFlags.NonPublic);
                
                if (prefix != null)
                {
                    _harmony.Patch(targetMethod, prefix: new HarmonyMethod(prefix));
                    _log?.LogInfo($"[AddressablePatches] Patched ProvideResource");
                }
            }
            catch (Exception ex)
            {
                _log?.LogWarning($"[AddressablePatches] Could not patch ProvideResource: {ex.Message}");
            }
        }
        
        private static void TryPatchTransformInternalId(MethodInfo targetMethod)
        {
            try
            {
                var postfix = typeof(AddressablePatches).GetMethod(
                    nameof(TransformInternalId_Postfix), 
                    BindingFlags.Static | BindingFlags.NonPublic);
                
                if (postfix != null)
                {
                    _harmony.Patch(targetMethod, postfix: new HarmonyMethod(postfix));
                    _log?.LogInfo($"[AddressablePatches] Patched TransformInternalId");
                }
            }
            catch (Exception ex)
            {
                _log?.LogWarning($"[AddressablePatches] Could not patch TransformInternalId: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Prefix for ProvideResource - logs all Addressables loads for discovery
        /// </summary>
        private static void ProvideResource_Prefix(IResourceLocation location)
        {
            if (location == null) return;
            
            try
            {
                var primaryKey = location.PrimaryKey ?? "";
                var internalId = location.InternalId ?? "";
                
                if (_verboseLogging && _discoveryLogCount < 200)
                {
                    // Filter out noise
                    if (!primaryKey.StartsWith("UnityEngine.") && 
                        !primaryKey.StartsWith("System.") &&
                        !internalId.Contains("unity_builtin"))
                    {
                        _log?.LogInfo($"[Addressables] Load: {primaryKey} (path: {internalId})");
                        _discoveryLogCount++;
                    }
                }
            }
            catch (Exception ex)
            {
                _log?.LogWarning($"[AddressablePatches] ProvideResource_Prefix error: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Postfix for TransformInternalId - can redirect asset paths to mod files
        /// </summary>
        private static void TransformInternalId_Postfix(IResourceLocation location, ref string __result)
        {
            if (location == null || string.IsNullOrEmpty(__result)) return;
            
            try
            {
                var primaryKey = location.PrimaryKey ?? "";
                
                // Check if we have an override for this asset
                if (AssetOverrideRegistry.HasOverride(primaryKey))
                {
                    var overridePath = AssetOverrideRegistry.GetOverrideFilePath(primaryKey);
                    if (!string.IsNullOrEmpty(overridePath))
                    {
                        _log?.LogInfo($"[Addressables] OVERRIDE: {primaryKey} -> {overridePath}");
                        __result = overridePath;
                        return;
                    }
                }
                
                // Also check by internal ID
                if (AssetOverrideRegistry.HasOverride(__result))
                {
                    var overridePath = AssetOverrideRegistry.GetOverrideFilePath(__result);
                    if (!string.IsNullOrEmpty(overridePath))
                    {
                        _log?.LogInfo($"[Addressables] OVERRIDE: {__result} -> {overridePath}");
                        __result = overridePath;
                    }
                }
            }
            catch (Exception ex)
            {
                _log?.LogWarning($"[AddressablePatches] TransformInternalId_Postfix error: {ex.Message}");
            }
        }
        
        public static void SetVerboseLogging(bool enabled)
        {
            _verboseLogging = enabled;
            _log?.LogInfo($"[AddressablePatches] Verbose logging = {enabled}");
        }
    }
}
