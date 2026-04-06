using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UI;

namespace HoboModPlugin.Framework
{
    /// <summary>
    /// Patches for GUIDatabase to enable sprite/icon overrides.
    /// GUIDatabase.spriteItemsCollection contains all item icons.
    /// This is the primary mechanism for replacing item icons in the game.
    /// </summary>
    public static class GUIDatabasePatches
    {
        private static ManualLogSource _log;
        private static Harmony _harmony;
        private static bool _initialized = false;
        
        // Pending sprite overrides: itemId -> sprite
        private static readonly Dictionary<int, Sprite> _pendingSpriteOverrides = new();
        
        // Pending sprite overrides by name: spriteName -> sprite
        private static readonly Dictionary<string, Sprite> _pendingSpriteOverridesByName = new();
        
        public static void Initialize(ManualLogSource log)
        {
            _log = log;
            _harmony = new Harmony("com.hobomod.guidatabasepatches");
            
            if (Plugin.EnableDebugMode.Value) _log?.LogInfo("[GUIDatabasePatches] Initializing...");
            
            ApplyPatches();
            

        }
        
        private static void ApplyPatches()
        {
            try
            {
                // Find GUIDatabase type
                var guiDatabaseType = typeof(GUIDatabase);
                
                if (guiDatabaseType == null)
                {
                    _log?.LogWarning("[GUIDatabasePatches] GUIDatabase type not found");
                    return;
                }
                
                // Patch OnAwake or AssignSprites to inject after sprites are loaded
                var onAwakeMethod = guiDatabaseType.GetMethod("OnAwake", 
                    BindingFlags.Public | BindingFlags.Instance);
                    
                if (onAwakeMethod != null)
                {
                    var postfix = typeof(GUIDatabasePatches).GetMethod(
                        nameof(OnAwake_Postfix), 
                        BindingFlags.Static | BindingFlags.NonPublic);
                        
                    _harmony.Patch(onAwakeMethod, postfix: new HarmonyMethod(postfix));
                    if (Plugin.EnableDebugMode.Value) _log?.LogInfo("[GUIDatabasePatches] Patched GUIDatabase.OnAwake");
                }
                else
                {
                    _log?.LogWarning("[GUIDatabasePatches] GUIDatabase.OnAwake not found");
                }
                
                // Also patch AssignSprites as a fallback
                var assignSpritesMethod = guiDatabaseType.GetMethod("AssignSprites", 
                    BindingFlags.Public | BindingFlags.Instance);
                    
                if (assignSpritesMethod != null)
                {
                    var postfix = typeof(GUIDatabasePatches).GetMethod(
                        nameof(AssignSprites_Postfix), 
                        BindingFlags.Static | BindingFlags.NonPublic);
                        
                    _harmony.Patch(assignSpritesMethod, postfix: new HarmonyMethod(postfix));
                    if (Plugin.EnableDebugMode.Value) _log?.LogInfo("[GUIDatabasePatches] Patched GUIDatabase.AssignSprites");
                }
            }
            catch (Exception ex)
            {
                _log?.LogError($"[GUIDatabasePatches] Error during patching: {ex.Message}");
                _log?.LogError(ex.StackTrace);
            }
        }
        
        /// <summary>
        /// Register a sprite override for an item by ID.
        /// The sprite will be injected when GUIDatabase loads.
        /// </summary>
        /// <param name="itemId">Item ID (index in spriteItemsCollection)</param>
        /// <param name="sprite">Replacement sprite</param>
        public static void RegisterSpriteOverride(int itemId, Sprite sprite)
        {
            if (sprite == null) return;
            
            _pendingSpriteOverrides[itemId] = sprite;
            _log?.LogInfo($"[GUIDatabasePatches] Registered sprite override for item {itemId}");
            
            // If GUIDatabase already initialized, apply immediately
            if (_initialized && GUIDatabase.Instance != null)
            {
                ApplySpriteOverride(itemId, sprite);
            }
        }
        
        /// <summary>
        /// Register a sprite override by sprite name.
        /// Useful for replacing non-item sprites in GUIDatabase.
        /// </summary>
        /// <param name="spriteName">Name of the sprite field (e.g., "questIcon_sprite")</param>
        /// <param name="sprite">Replacement sprite</param>
        public static void RegisterSpriteOverrideByName(string spriteName, Sprite sprite)
        {
            if (string.IsNullOrEmpty(spriteName) || sprite == null) return;
            
            _pendingSpriteOverridesByName[spriteName] = sprite;
            _log?.LogInfo($"[GUIDatabasePatches] Registered sprite override for {spriteName}");
            
            // If GUIDatabase already initialized, apply immediately
            if (_initialized && GUIDatabase.Instance != null)
            {
                ApplySpriteOverrideByName(spriteName, sprite);
            }
        }
        
        // ==================== Patch Methods ====================
        
        private static void OnAwake_Postfix(GUIDatabase __instance)
        {
            if (Plugin.EnableDebugMode.Value) _log?.LogInfo("[GUIDatabasePatches] GUIDatabase.OnAwake called");
            ApplyAllPendingOverrides(__instance);
        }
        
        private static void AssignSprites_Postfix(GUIDatabase __instance)
        {
            if (Plugin.EnableDebugMode.Value) _log?.LogInfo("[GUIDatabasePatches] GUIDatabase.AssignSprites called");
            ApplyAllPendingOverrides(__instance);
        }
        
        private static void ApplyAllPendingOverrides(GUIDatabase instance)
        {
            if (instance == null) return;
            
            _initialized = true;
            
            // Log sprite collection info for discovery
            if (instance.spriteItemsCollection != null)
            {
                if (Plugin.EnableDebugMode.Value) _log?.LogInfo($"[GUIDatabasePatches] spriteItemsCollection has {instance.spriteItemsCollection.Length} sprites");
                
                // Commented out verbose sprite list dumping
                // for (int i = 0; i < Math.Min(10, instance.spriteItemsCollection.Length); i++)
                // {
                //     var sprite = instance.spriteItemsCollection[i];
                //     if (sprite != null)
                //     {
                //         _log?.LogInfo($"  [{i}] = {sprite.name}");
                //     }
                // }
            }
            else
            {
                _log?.LogWarning("[GUIDatabasePatches] spriteItemsCollection is null!");
            }
            
            // Apply pending item sprite overrides
            foreach (var kvp in _pendingSpriteOverrides)
            {
                ApplySpriteOverride(kvp.Key, kvp.Value);
            }
            
            // Apply pending named sprite overrides
            foreach (var kvp in _pendingSpriteOverridesByName)
            {
                ApplySpriteOverrideByName(kvp.Key, kvp.Value);
            }
        }
        
        private static void ApplySpriteOverride(int itemId, Sprite sprite)
        {
            try
            {
                var instance = GUIDatabase.Instance;
                if (instance?.spriteItemsCollection == null) return;
                
                if (itemId < 0 || itemId >= instance.spriteItemsCollection.Length)
                {
                    _log?.LogWarning($"[GUIDatabasePatches] Item ID {itemId} out of range (max: {instance.spriteItemsCollection.Length - 1})");
                    return;
                }
                
                var oldSprite = instance.spriteItemsCollection[itemId];
                instance.spriteItemsCollection[itemId] = sprite;
                
                _log?.LogInfo($"[GUIDatabasePatches] APPLIED sprite override for item {itemId}: {oldSprite?.name ?? "null"} -> {sprite.name}");
            }
            catch (Exception ex)
            {
                _log?.LogError($"[GUIDatabasePatches] Failed to apply sprite override for item {itemId}: {ex.Message}");
            }
        }
        
        private static void ApplySpriteOverrideByName(string spriteName, Sprite sprite)
        {
            try
            {
                var instance = GUIDatabase.Instance;
                if (instance == null) return;
                
                // Use reflection to find and set the sprite field
                var field = typeof(GUIDatabase).GetField(spriteName, 
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    
                if (field != null && field.FieldType == typeof(Sprite))
                {
                    var oldSprite = field.GetValue(instance) as Sprite;
                    field.SetValue(instance, sprite);
                    _log?.LogInfo($"[GUIDatabasePatches] APPLIED sprite override for field {spriteName}: {oldSprite?.name ?? "null"} -> {sprite.name}");
                }
                else
                {
                    _log?.LogWarning($"[GUIDatabasePatches] Sprite field '{spriteName}' not found");
                }
            }
            catch (Exception ex)
            {
                _log?.LogError($"[GUIDatabasePatches] Failed to apply sprite override for {spriteName}: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Get count of registered overrides
        /// </summary>
        public static int OverrideCount => _pendingSpriteOverrides.Count + _pendingSpriteOverridesByName.Count;
        
        /// <summary>
        /// Clear all pending overrides
        /// </summary>
        public static void ClearOverrides()
        {
            _pendingSpriteOverrides.Clear();
            _pendingSpriteOverridesByName.Clear();
            _log?.LogInfo("[GUIDatabasePatches] Cleared all overrides");
        }
    }
}
