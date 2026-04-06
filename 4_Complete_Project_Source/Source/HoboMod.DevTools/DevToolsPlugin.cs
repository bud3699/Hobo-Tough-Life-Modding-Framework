using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using UnityEngine;
using System.IO;
using HoboModPlugin;
using HoboModPlugin.Framework;
using HoboModPlugin.Features;

namespace HoboMod.DevTools
{
    [BepInPlugin(PLUGIN_GUID, PLUGIN_NAME, PLUGIN_VERSION)]
    public class DevToolsPlugin : BasePlugin
    {
        public const string PLUGIN_GUID = "com.hobomod.devtools";
        public const string PLUGIN_NAME = "HoboMod.DevTools";
        public const string PLUGIN_VERSION = "1.0.0";

        internal static new ManualLogSource Log;

        public override void Load()
        {
            Log = base.Log;
            var pluginPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
            var rootPath = Path.GetDirectoryName(pluginPath);
            
            // Initialize SceneDumper with this mod's path
            SceneDumper.Initialize(Log, rootPath);
            
            AddComponent<DevToolsUpdater>();
            Log.LogInfo($"{PLUGIN_NAME} v{PLUGIN_VERSION} loaded!");
        }
    }

    public class DevToolsUpdater : MonoBehaviour
    {
        public DevToolsUpdater(System.IntPtr ptr) : base(ptr) { }

        void Start()
        {
            // Log dev hotkeys from all mods (once)
            LogModDevHotkeys();
        }

        private void Update()
        {
            // Process dev hotkeys from ALL mods
            ProcessModDevHotkeys();

            // === MODEL DISCOVERY HOTKEYS ===
            // F10 - Identify mesh you're looking at (raycast)
            if (Input.GetKeyDown(KeyCode.F10))
            {
                IdentifyLookedAtMesh();
            }
            
            // F11 - List all meshes in scene (discovery tool for modders)
            // Shift+F11 = Dump Scene (Local: 100m radius)
            // Ctrl+F11 = Dump Scene (Full map)
            if (Input.GetKeyDown(KeyCode.F11))
            {
                if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
                {
                    DevToolsPlugin.Log.LogInfo("[F11+Shift] Full Scene Dump triggered");
                    SceneDumper.DumpScene(true); // Full dump
                }
                else
                {
                    DevToolsPlugin.Log.LogInfo("[F11] Filtered Scene Dump triggered");
                    SceneDumper.DumpScene(false); // Filtered dump (no player/NPCs)
                }
            }
            
            // F12 - Apply registered model overrides (or test with cube if none registered)
            if (Input.GetKeyDown(KeyCode.F12))
            {
                TestReplacePlayerModel();
            }
        }

        private bool _devHotkeysLogged = false;

        /// <summary>
        /// Log all dev hotkeys from loaded mods at startup
        /// </summary>
        private void LogModDevHotkeys()
        {
            if (_devHotkeysLogged) return;
            _devHotkeysLogged = true;
            
            // Plugin.Framework is internal static, generally accessible if internals visible
            // But DevTools is a separate assembly. 
            // Step 1 check: Is Plugin.Framework accessible? 
            // It is 'internal static'. We might need to use reflection or check InternalsVisibleTo.
            // For now, assuming it works or we fix accessibility.
            
            if (Plugin.Framework?.ModLoader?.LoadedMods == null) return;

            foreach (var mod in Plugin.Framework.ModLoader.LoadedMods)
            {
                if (mod.DevHotkeys == null || mod.DevHotkeys.Count == 0) continue;
                
                DevToolsPlugin.Log.LogInfo($"=== {mod.Name.ToUpper()} DEV HOTKEYS ===");
                foreach (var hotkey in mod.DevHotkeys)
                {
                    DevToolsPlugin.Log.LogInfo($"{hotkey.Key} - {hotkey.Action}: {hotkey.ItemId}");
                }
            }
        }
        
        /// <summary>
        /// Process dev hotkeys from all loaded mods
        /// </summary>
        private void ProcessModDevHotkeys()
        {
            if (Plugin.Framework?.ModLoader?.LoadedMods == null) return;

            foreach (var mod in Plugin.Framework.ModLoader.LoadedMods)
            {
                if (mod.DevHotkeys == null) continue;
                
                foreach (var hotkey in mod.DevHotkeys)
                {
                    KeyCode keyCode = ParseKeyCode(hotkey.Key);
                    if (keyCode != KeyCode.None && Input.GetKeyDown(keyCode))
                    {
                        switch (hotkey.Action?.ToLower())
                        {
                            case "spawn_item":
                                string fullItemId = $"{mod.Id}:{hotkey.ItemId}";
                                DevToolsPlugin.Log.LogInfo($"[{hotkey.Key}] Spawning mod item: {fullItemId}");
                                DebugTools.SpawnModItem(fullItemId);
                                break;
                            case "spawn_vanilla":
                                if (uint.TryParse(hotkey.ItemId, out uint vanillaId))
                                {
                                    DevToolsPlugin.Log.LogInfo($"[{hotkey.Key}] Spawning vanilla item ID: {vanillaId}");
                                    DebugTools.SpawnVanillaItem(vanillaId);
                                }
                                break;
                            case "explore_items":
                                DevToolsPlugin.Log.LogInfo($"[{hotkey.Key}] Exploring item database");
                                DebugTools.ExploreItemDatabase();
                                break;
                            case "search_items":
                                DevToolsPlugin.Log.LogInfo($"[{hotkey.Key}] Searching items by name");
                                DebugTools.SearchItemsByName();
                                break;
                            case "dump_items":
                                DevToolsPlugin.Log.LogInfo($"[{hotkey.Key}] Dumping items to file");
                                DebugTools.DumpAllItemsToFile();
                                break;
                            case "dump_stats":
                                DevToolsPlugin.Log.LogInfo($"[{hotkey.Key}] Dumping character stats");
                                DebugTools.DumpCharacterStats();
                                break;
                            case "start_quest":
                                string questId = $"{mod.Id}:{hotkey.ItemId}";
                                DevToolsPlugin.Log.LogInfo($"[{hotkey.Key}] Starting quest: {questId}");
                                DebugTools.TryStartTestQuest(questId);
                                break;
                            
                            // ModelTweaker actions
                            case "tweaker_enable":
                                ModelOverrideRegistry.ApplyAllOverrides();
                                ModelOverrideRegistry.EnableTweakMode();
                                break;
                            case "tweaker_scale_up":
                                ModelOverrideRegistry.TweakScale(0.01f);
                                break;
                            case "tweaker_scale_down":
                                ModelOverrideRegistry.TweakScale(-0.01f);
                                break;
                            case "tweaker_move_forward":
                                ModelOverrideRegistry.TweakPosition(new Vector3(0, 0, 0.05f));
                                break;
                            case "tweaker_move_back":
                                ModelOverrideRegistry.TweakPosition(new Vector3(0, 0, -0.05f));
                                break;
                            case "tweaker_move_left":
                                ModelOverrideRegistry.TweakPosition(new Vector3(-0.05f, 0, 0));
                                break;
                            case "tweaker_move_right":
                                ModelOverrideRegistry.TweakPosition(new Vector3(0.05f, 0, 0));
                                break;
                            case "tweaker_move_up":
                                ModelOverrideRegistry.TweakPosition(new Vector3(0, 0.05f, 0));
                                break;
                            case "tweaker_move_down":
                                ModelOverrideRegistry.TweakPosition(new Vector3(0, -0.05f, 0));
                                break;
                            
                            // Rotation tweaks (15 degrees per press)
                            case "tweaker_rot_x_plus":
                                ModelOverrideRegistry.TweakRotation(15f, 0, 0);
                                break;
                            case "tweaker_rot_x_minus":
                                ModelOverrideRegistry.TweakRotation(-15f, 0, 0);
                                break;
                            case "tweaker_rot_y_plus":
                                ModelOverrideRegistry.TweakRotation(0, 15f, 0);
                                break;
                            case "tweaker_rot_y_minus":
                                ModelOverrideRegistry.TweakRotation(0, -15f, 0);
                                break;
                            case "tweaker_rot_z_plus":
                                ModelOverrideRegistry.TweakRotation(0, 0, 15f);
                                break;
                            case "tweaker_rot_z_minus":
                                ModelOverrideRegistry.TweakRotation(0, 0, -15f);
                                break;
                            
                            case "tweaker_print":
                                ModelOverrideRegistry.PrintTweakValues();
                                break;
                            
                            default:
                                DevToolsPlugin.Log.LogWarning($"Unknown hotkey action: {hotkey.Action}");
                                break;
                        }
                    }
                }
            }
        }
        
        /// <summary>
        /// Parse string key name to KeyCode enum
        /// </summary>
        private KeyCode ParseKeyCode(string keyName)
        {
            if (string.IsNullOrEmpty(keyName)) return KeyCode.None;
            if (System.Enum.TryParse<KeyCode>(keyName, true, out var result))
                return result;
            return KeyCode.None;
        }

        /// <summary>
        /// Identify mesh you're looking at (F10) - finds camera and uses raycast
        /// </summary>
        private void IdentifyLookedAtMesh()
        {
            DevToolsPlugin.Log.LogInfo("=== MESH IDENTIFICATION (F10) ===");
            
            // Find any camera in the scene
            Camera cam = Camera.main;
            if (cam == null)
            {
                // Try to find any camera
                var cameras = UnityEngine.Object.FindObjectsOfType<Camera>();
                if (cameras != null && cameras.Length > 0)
                {
                    // Find the one that's enabled and rendering
                    foreach (var c in cameras)
                    {
                        if (c != null && c.enabled && c.gameObject.activeInHierarchy)
                        {
                            cam = c;
                            DevToolsPlugin.Log.LogInfo($"Using camera: {c.name} (depth: {c.depth})");
                            break;
                        }
                    }
                }
            }
            
            if (cam == null)
            {
                // Still no camera - list all cameras for debugging
                var allCams = UnityEngine.Object.FindObjectsOfType<Camera>();
                DevToolsPlugin.Log.LogWarning($"No active camera found! Found {allCams?.Length ?? 0} cameras total:");
                if (allCams != null)
                {
                    foreach (var c in allCams)
                    {
                        DevToolsPlugin.Log.LogInfo($"  Camera: {c?.name ?? "(null)"} enabled={c?.enabled} active={c?.gameObject.activeInHierarchy}");
                    }
                }
                
                // Fallback: just list all nearby skinned meshes
                DevToolsPlugin.Log.LogInfo("Listing ALL skinned meshes in scene:");
                var allSkinned = UnityEngine.Object.FindObjectsOfType<SkinnedMeshRenderer>();
                foreach (var smr in allSkinned)
                {
                    if (smr?.sharedMesh != null)
                    {
                        DevToolsPlugin.Log.LogInfo($"  SKINNED: \"{smr.sharedMesh.name}\" ({smr.gameObject.name})");
                    }
                }
                DevToolsPlugin.Log.LogInfo("=== END ===");
                return;
            }
            
            DevToolsPlugin.Log.LogInfo($"Camera position: {cam.transform.position}");
            DevToolsPlugin.Log.LogInfo($"Camera forward: {cam.transform.forward}");
            DevToolsPlugin.Log.LogInfo("Nearby meshes:");
            
            // Since Physics.Raycast might not work, use distance-based detection relative to camera forward
            Vector3 lookPoint = cam.transform.position + cam.transform.forward * 3f;
            
            int found = 0;
            
            // Check nearby skinned meshes (NPCs, characters)
            var skinnedRenderers = UnityEngine.Object.FindObjectsOfType<SkinnedMeshRenderer>();
            foreach (var smr in skinnedRenderers)
            {
                if (smr == null || smr.sharedMesh == null) continue;
                float dist = Vector3.Distance(lookPoint, smr.transform.position);
                if (dist < 8f)
                {
                    DevToolsPlugin.Log.LogInfo($"  SKINNED [{dist:F1}m]: \"{smr.sharedMesh.name}\" ({smr.gameObject.name})");
                    found++;
                }
            }
            
            // Check nearby static meshes
            var meshFilters = UnityEngine.Object.FindObjectsOfType<MeshFilter>();
            foreach (var mf in meshFilters)
            {
                if (mf == null || mf.sharedMesh == null) continue;
                float dist = Vector3.Distance(lookPoint, mf.transform.position);
                if (dist < 5f && found < 15)
                {
                    DevToolsPlugin.Log.LogInfo($"  STATIC [{dist:F1}m]: \"{mf.sharedMesh.name}\" ({mf.gameObject.name})");
                    found++;
                }
            }
            
            if (found == 0)
                DevToolsPlugin.Log.LogInfo("  No meshes found near look point - move closer");
                
            DevToolsPlugin.Log.LogInfo("=== END ===");
        }
        
        /// <summary>
        /// Apply registered model overrides, or test with cube if none registered
        /// </summary>
        private void TestReplacePlayerModel()
        {
            DevToolsPlugin.Log.LogInfo("[F12] === APPLYING MODEL OVERRIDES ===");
            
            try
            {
                // First, try to apply any registered overrides from mods
                ModelOverrideRegistry.ApplyAllOverrides();
                
                // Then, for testing, also show what meshes are available
                var skinRenderers = UnityEngine.Object.FindObjectsOfType<SkinnedMeshRenderer>();
                DevToolsPlugin.Log.LogInfo($"  Scene has {skinRenderers?.Length ?? 0} SkinnedMeshRenderer(s)");
                
                if (skinRenderers != null && skinRenderers.Length > 0)
                {
                    foreach (var renderer in skinRenderers)
                    {
                        if (renderer == null) continue;
                        var meshName = renderer.sharedMesh?.name ?? "(null)";
                        
                        // Check if this mesh has an override
                        if (ModelOverrideRegistry.HasOverride(meshName))
                        {
                            DevToolsPlugin.Log.LogInfo($"    [OVERRIDE ACTIVE] {meshName}");
                        }
                    }
                }
                
                DevToolsPlugin.Log.LogInfo("[F12] Done!");
            }
            catch (System.Exception ex)
            {
                DevToolsPlugin.Log.LogError($"[F12] Error: {ex.Message}");
                DevToolsPlugin.Log.LogError(ex.StackTrace);
            }
        }
    }
}
